# Configure sign-in

Everyone who signs in to Logbook has full access: to all logs, and to manage Applications and API Keys. There are two ways to sign in, and you can use both. All settings are in the [configuration reference](../reference/configuration.md#sign-in).

## Local administrator

Set a password of at least 12 characters. The name is `admin` unless you change it:

```sh
SignIn__Administrator__Password=…
SignIn__Administrator__UserName=admin
```

To change the password, set the new one and restart Logbook. This also ends all sessions of the local administrator. Failed attempts are limited per IP address, so [behind a proxy](reverse-proxy.md#tell-logbook-about-the-proxy) the forwarded headers must be set up.

## OpenID Connect

With an OpenID Connect provider such as Authora, people sign in with their own account. Logbook needs an authorization code client at the provider.

1. Register a client with this redirect address, using the address of your Logbook:

   ```text
   https://logbook.example.com/api/v1/auth/oidc/callback
   ```

2. Decide who gets access. Logbook requires one claim with one value, for example membership in a group. This is not optional, because signing in gives full access, and many providers let anyone create an account. The claim can come from the ID token or the UserInfo endpoint, and can be a list. Check that your provider can only issue it to the right people, e.g. do not rely on an `email` the user can choose without it being verified.

3. Configure Logbook:

   ```sh
   SignIn__OpenIdConnect__Authority=https://id.example.com
   SignIn__OpenIdConnect__ClientId=logbook
   SignIn__OpenIdConnect__ClientSecret=…
   SignIn__OpenIdConnect__DisplayName=Authora
   SignIn__OpenIdConnect__RequiredClaimType=groups
   SignIn__OpenIdConnect__RequiredClaimValue=logbook-admins
   ```

   If the provider only issues the claim for a certain scope, add it with `SignIn__OpenIdConnect__AdditionalScopes__0=groups`.

4. Restart Logbook. The sign-in page now shows a button with the name of the provider.

Someone without the claim sees that they have no access, and the log has a warning with their `sub`. Once this works, you can turn off the local administrator by removing its password.

Logbook checks the claim only when someone signs in. Someone you remove at the provider keeps access until their session ends, by default after 12 hours ([`SignIn:SessionLifetime`](../reference/configuration.md#sign-in)). To end all sessions at once, remove the rows of `config.data_protection_keys`, which also ends those of the local administrator, and restart Logbook. Signing out of Logbook does not sign out of the provider.

## Scripts

A script signs in like a browser, and keeps the cookie. Requests that change something also need the header `X-Requested-With`, with any value:

```sh
curl --cookie-jar cookies.txt -X POST "$LOGBOOK_URL/api/v1/auth/sign-in" \
  -H "Content-Type: application/json" \
  -d "{\"userName\":\"admin\",\"password\":\"$LOGBOOK_ADMIN_PASSWORD\"}"

curl --cookie cookies.txt -X POST "$LOGBOOK_URL/api/v1/applications" \
  -H "X-Requested-With: curl" \
  -H "Content-Type: application/json" \
  -d '{"name":"Shop Web App"}'
```

The file `cookies.txt` gives full access until the session ends, so treat it like a password.
