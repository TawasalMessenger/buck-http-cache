IF NOT EXIST paket.lock (
    START /WAIT .paket/paket.exe install
)
dotnet restore src/BuckHTTPCache
dotnet build src/BuckHTTPCache

