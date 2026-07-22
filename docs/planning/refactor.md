# Cycle Resolution Plan

Lifeblood identifies 7 cycles (all `LikelyRealLoop`). Three are genuine production architectural loops in the gateway and auth code — the targets of this plan. The other four are lower value: one is the same gateway trio counted again at type granularity (it disappears when the loop below is fixed), one is a nested-class loop inside `StreamingBodyCapturePlugin`, and two are test-only nested-class loops. By applying the "ponytail" strategy (shortest path, zero boilerplate), we untangle the three production loops cleanly.

## Proposed Changes

### 1. The Gateway Route Translator Cycle
`GatewayRouteTable` → `YarpConfigTranslator` → `ConsecutiveFailuresHealthPolicy` (for `PolicyName` constant) → `GatewayRouteTable` (for route lookups).

**[MODIFY]** `src/ConduitSharp.Gateway.AspNetCore/Proxy/YarpConfigTranslator.cs`
- Instead of referencing `ConsecutiveFailuresHealthPolicy.PolicyName`, hardcode the `"ConsecutiveFailures"` string, or declare the constant here and reference it in the policy. The absolute simplest way is to just use the literal string `"ConsecutiveFailures"` in both places. It is a single-use internal policy name. This completely severs the translator's dependency on the policy class.

**[MODIFY]** `src/ConduitSharp.Gateway.AspNetCore/Proxy/ConsecutiveFailuresHealthPolicy.cs`
- Replace `public const string PolicyName = "ConsecutiveFailures";` with the literal property `public string Name => "ConsecutiveFailures";`.

---

### 2. The JWT Plugin Cycle
`JwtAuthPlugin` → `JwtAuthHandler` → `JwtProviderConfig` (which lives in `JwtAuthPlugin.cs`).

**[NEW]** `src/ConduitSharp.Security/Jwt/JwtAuthConfig.cs`
- Move the `JwtProviderConfig` and `JwtAuthConfig` records out of `JwtAuthPlugin.cs` and into this new dedicated configuration file.

**[MODIFY]** `src/ConduitSharp.Security/Jwt/JwtAuthPlugin.cs`
- Remove the configuration records from this file. The plugin will now depend on the handler and the config, and the handler will only depend on the config. Cycle broken.

---

### 3. The JWKS Plugin Cycle
`JwksJwtAuthPlugin` → `JwksJwtAuthHandler` → `JwksProviderConfig` (which lives in `JwksJwtAuthPlugin.cs`).

**[NEW]** `src/ConduitSharp.Security/Jwt/JwksJwtAuthConfig.cs`
- Move the `JwksProviderConfig` and `JwksJwtAuthConfig` records out of `JwksJwtAuthPlugin.cs` and into this new dedicated configuration file.

**[MODIFY]** `src/ConduitSharp.Security/Jwt/JwksJwtAuthPlugin.cs`
- Remove the configuration records from this file. The cycle is broken symmetrically to the standard JWT plugin.

## Verification Plan
1. **Compile**: `dotnet build` to ensure the config extraction doesn't break any usages.
2. **Test**: `dotnet test` to ensure existing auth and gateway tests still pass.
3. **Analyze**: Run the Lifeblood CLI (`~/.dotnet/tools/lifeblood analyze --project .`) to verify that the cycles are resolved. The output should read `Cycles: 3` (down from 7). Fixing the gateway loop removes two entries — it is counted at both file and type granularity — and the Jwt and Jwks loops remove one each, so four cycles clear. The three that remain are the nested-class loop in `StreamingBodyCapturePlugin` and the two test-only nested-class loops, which this plan does not touch.
