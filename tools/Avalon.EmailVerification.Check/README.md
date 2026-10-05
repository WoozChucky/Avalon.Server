# Disposable email-verification check

This opt-in tool stays outside the unit-test solution: it uses actual PostgreSQL migrations and transactions. It refuses non-loopback hosts, any database except `avalon_email_verification_test`, and a nonempty database on the first check. It never deletes a database or reads normal application configuration/secrets.

Start a **new disposable** PostgreSQL container with that database. Set `AVALON_TEST_AUTH_DATABASE` in the current process to its loopback connection, then run:

```powershell
dotnet run --project tools/Avalon.EmailVerification.Check
```

The checks migrate the historical auth schema, preserve the seed's credentials and roles, upgrade to email verification, and exercise cross-connection issuance/consumption/replacement/cleanup. It also sends real Development Pickup mail, consumes proof only explicitly, and checks that no license was created. Output contains check names only, no proof or mail contents. No tests silently skip when configuration is missing.

Optional website smoke check: keep the same disposable database and run `dotnet run --project tools/Avalon.EmailVerification.Check -- --serve`. Run the public app with `VITE_API_TARGET=http://127.0.0.1:5214`. Its local fixture accepts dummy sign-in only and serves one `EMAILSMOKE` account. This host is never a production authentication test or deployable API; the normal authentication regression suites cover real sign-in. Mail is under the local temporary directory `avalon-email-smoke-pickup`; open the link without logging its contents, click confirmation, and inspect the account state. Stop both local hosts, delete their generated mail files, and remove only the exact disposable container you created.

Real Resend delivery is a separate operator test after domain/key setup; this checker never sends through Resend.
