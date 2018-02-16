Please use this repo, it has a cleaner API interface and any future investments will be made there: https://github.com/dmarlow/AspNetTicketBridge

# BearerTokenBridge
Decrypts an OWIN OAuth Bearer token to be used in ASP.NET Core. 

This MachineKey decryptor uses HMACSHA1 and AES with the decryptionKey and validationKey from your web.config to decrypt OAuth tokens generated from OWIN.

`<machineKey compatibilityMode="Framework45" decryption="AES" decryptionKey="....." validation="SHA1" validationKey="....." />`

## Example

```
string validationKey = "<value from old web.config/machineKey/validationKey>";
string key = "<value from old web.config/machineKey/decryptionKey>";

// Decrypt the token
var ticket = MachineKeyOwinBearerAuthTicketUnprotector.Unprotect(token, key, validationKey);

// The ticket is in v3 format and needs to be converted to v5 (ASP.NET Core 2.0).
var newTicket = MachineKeyOwinBearerAuthTicketUnprotector.Convert(ticket, "Degreed");

// If using a custom AuthenticationHandler, you can return the new ticket 
// in the HandleAuthenticateAsync method.
var result = AuthenticateResult.Success(newTicket);
```


Special thanks for [Umar Karimabadi](https://stackoverflow.com/users/7310452/umar-karimabadi) for doing all of the heavy lifting. For more information, see here: https://stackoverflow.com/questions/46546254/using-machine-keys-for-idataprotector-asp-net-core
