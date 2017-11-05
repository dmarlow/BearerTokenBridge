﻿using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace BearerTokenBridge
{
    public static class MachineKey
    {
        private static readonly UTF8Encoding SecureUTF8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        public static byte[] Unprotect(byte[] protectedData, string validationKey, string encKey, string primaryPurpose, params string[] specificPurposes)
        {
            // The entire operation is wrapped in a 'checked' block because any overflows should be treated as failures.
            checked
            {
                using (SymmetricAlgorithm decryptionAlgorithm = new AesCryptoServiceProvider())
                {
                    decryptionAlgorithm.Key = SP800_108.DeriveKey(HexToBinary(encKey), primaryPurpose, specificPurposes);

                    // These KeyedHashAlgorithm instances are single-use; we wrap it in a 'using' block.
                    using (KeyedHashAlgorithm validationAlgorithm = new HMACSHA1())
                    {
                        validationAlgorithm.Key = SP800_108.DeriveKey(HexToBinary(validationKey), primaryPurpose, specificPurposes);

                        int ivByteCount = decryptionAlgorithm.BlockSize / 8;
                        int signatureByteCount = validationAlgorithm.HashSize / 8;
                        int encryptedPayloadByteCount = protectedData.Length - ivByteCount - signatureByteCount;
                        if (encryptedPayloadByteCount <= 0)
                        {
                            return null;
                        }

                        byte[] computedSignature = validationAlgorithm.ComputeHash(protectedData, 0, ivByteCount + encryptedPayloadByteCount);

                        if (!BuffersAreEqual(
                            buffer1: protectedData, buffer1Offset: ivByteCount + encryptedPayloadByteCount, buffer1Count: signatureByteCount,
                            buffer2: computedSignature, buffer2Offset: 0, buffer2Count: computedSignature.Length))
                        {

                            return null;
                        }

                        byte[] iv = new byte[ivByteCount];
                        Buffer.BlockCopy(protectedData, 0, iv, 0, iv.Length);
                        decryptionAlgorithm.IV = iv;

                        using (MemoryStream memStream = new MemoryStream())
                        {
                            using (ICryptoTransform decryptor = decryptionAlgorithm.CreateDecryptor())
                            {
                                using (CryptoStream cryptoStream = new CryptoStream(memStream, decryptor, CryptoStreamMode.Write))
                                {
                                    cryptoStream.Write(protectedData, ivByteCount, encryptedPayloadByteCount);
                                    cryptoStream.FlushFinalBlock();

                                    byte[] clearData = memStream.ToArray();

                                    return clearData;
                                }
                            }
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoOptimization)]
        private static bool BuffersAreEqual(byte[] buffer1, int buffer1Offset, int buffer1Count, byte[] buffer2, int buffer2Offset, int buffer2Count)
        {
            bool success = (buffer1Count == buffer2Count); // can't possibly be successful if the buffers are of different lengths
            for (int i = 0; i < buffer1Count; i++)
            {
                success &= (buffer1[buffer1Offset + i] == buffer2[buffer2Offset + (i % buffer2Count)]);
            }
            return success;
        }

        private static class SP800_108
        {
            public static byte[] DeriveKey(byte[] keyDerivationKey, string primaryPurpose, params string[] specificPurposes)
            {
                using (HMACSHA512 hmac = new HMACSHA512(keyDerivationKey))
                {

                    GetKeyDerivationParameters(out byte[] label, out byte[] context, primaryPurpose, specificPurposes);

                    byte[] derivedKey = DeriveKeyImpl(hmac, label, context, keyDerivationKey.Length * 8);

                    return derivedKey;
                }
            }

            private static byte[] DeriveKeyImpl(HMAC hmac, byte[] label, byte[] context, int keyLengthInBits)
            {
                checked
                {
                    int labelLength = (label != null) ? label.Length : 0;
                    int contextLength = (context != null) ? context.Length : 0;
                    byte[] buffer = new byte[4 /* [i]_2 */ + labelLength /* label */ + 1 /* 0x00 */ + contextLength /* context */ + 4 /* [L]_2 */];

                    if (labelLength != 0)
                    {
                        Buffer.BlockCopy(label, 0, buffer, 4, labelLength); // the 4 accounts for the [i]_2 length
                    }
                    if (contextLength != 0)
                    {
                        Buffer.BlockCopy(context, 0, buffer, 5 + labelLength, contextLength); // the '5 +' accounts for the [i]_2 length, the label, and the 0x00 byte
                    }
                    WriteUInt32ToByteArrayBigEndian((uint)keyLengthInBits, buffer, 5 + labelLength + contextLength); // the '5 +' accounts for the [i]_2 length, the label, the 0x00 byte, and the context

                    int numBytesWritten = 0;
                    int numBytesRemaining = keyLengthInBits / 8;
                    byte[] output = new byte[numBytesRemaining];

                    for (uint i = 1; numBytesRemaining > 0; i++)
                    {
                        WriteUInt32ToByteArrayBigEndian(i, buffer, 0); // set the first 32 bits of the buffer to be the current iteration value
                        byte[] K_i = hmac.ComputeHash(buffer);

                        // copy the leftmost bits of K_i into the output buffer
                        int numBytesToCopy = Math.Min(numBytesRemaining, K_i.Length);
                        Buffer.BlockCopy(K_i, 0, output, numBytesWritten, numBytesToCopy);
                        numBytesWritten += numBytesToCopy;
                        numBytesRemaining -= numBytesToCopy;
                    }

                    // finished
                    return output;
                }
            }

            private static void WriteUInt32ToByteArrayBigEndian(uint value, byte[] buffer, int offset)
            {
                buffer[offset + 0] = (byte)(value >> 24);
                buffer[offset + 1] = (byte)(value >> 16);
                buffer[offset + 2] = (byte)(value >> 8);
                buffer[offset + 3] = (byte)(value);
            }

        }

        private static void GetKeyDerivationParameters(out byte[] label, out byte[] context, string primaryPurpose, params string[] specificPurposes)
        {
            label = SecureUTF8Encoding.GetBytes(primaryPurpose);

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, SecureUTF8Encoding))
            {
                foreach (string specificPurpose in specificPurposes)
                {
                    writer.Write(specificPurpose);
                }
                context = stream.ToArray();
            }
        }

        private static byte[] HexToBinary(string data)
        {
            if (data == null || data.Length % 2 != 0)
            {
                // input string length is not evenly divisible by 2
                return null;
            }

            byte[] binary = new byte[data.Length / 2];

            for (int i = 0; i < binary.Length; i++)
            {
                int highNibble = HexToInt(data[2 * i]);
                int lowNibble = HexToInt(data[2 * i + 1]);

                if (highNibble == -1 || lowNibble == -1)
                {
                    return null; // bad hex data
                }
                binary[i] = (byte)((highNibble << 4) | lowNibble);
            }

            int HexToInt(char h)
            {
                return (h >= '0' && h <= '9') ? h - '0' :
                (h >= 'a' && h <= 'f') ? h - 'a' + 10 :
                (h >= 'A' && h <= 'F') ? h - 'A' + 10 :
                -1;
            }
            return binary;
        }
    }
}
