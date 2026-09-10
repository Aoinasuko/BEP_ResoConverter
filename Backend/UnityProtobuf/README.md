# Unity protobuf runtime

Vendored Google.Protobuf v30.1 / NuGet 3.30.1, commit
`0d815c5b74281f081c1ee4b431a4d5bbb1615c97`, BSD-3-Clause.

Built for Unity 2022.3's .NET Standard 2.1 profile, without optional fast-string/SIMD
paths. Five unaligned primitive accesses use the equivalent `MemoryMarshal.Read`
and `MemoryMarshal.Write` APIs, and the ASCII writer uses a bounds-checked span loop
instead of Unsafe pointer arithmetic. This removes the external Unsafe assembly dependency
and avoids conflicts with VRChat SDK / NDMF / Unity MCP copies of that DLL.
The bundled upstream signing key preserves the schema's assembly identity.

```powershell
dotnet build Backend/UnityProtobuf/UnityProtobuf.csproj -c Release
```
