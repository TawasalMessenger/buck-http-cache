# BuckHTTPCache
Implementation of [HTTP Cache API by Buck](https://buck.build/concept/http_cache_api.html) for using as a high performance cache service.

It was born after trying to use [Uber's HTTP Cache API](https://github.com/uber-archive/buck-http-cache/) - unsuccessfully.
 
A [Giraffe](https://github.com/giraffe-fsharp/Giraffe) web application, which has been created via the `dotnet new giraffe` command.

## Available features

- Get artifacts (download)
- Put artifacts (upload)
- Summary of currently stored keys/values
- Filesystem-backed cache
- Memory-backed cache (in-memory)

Also: settings via environments variables, aspnet.core full performance power, async interfaces, easily understandable & extendable

### Enhancements in mind:

- Database-backed cache(s)
- Supports for additional BUCK metadata (such as targets)
- 'Weak' points - cache errors, unused cache, etc.
- Purge artifacts unused for some time 
- More settings
- More statistics & metrics
- Multiple Nodes
- Dashboard (?)
- Tests
- Docs
- HTTPS


### Default settings:

- HOST: 127.0.0.1 aka localhost
- PORT: 5080
- CACHE_DIR: CurrentDirectory + "cache" (aka "./cache")

All of the above can be change via environment variables.


## Build and run the application

.NET Core 3.1 or higher is required.

Install [Paket](https://fsprojects.github.io/Paket/get-started.html) dependency manager:
```
dotnet new tool-manifest
dotnet tool install paket
dotnet tool restore
```

Install dependencies:
```
dotnet paket restore
```

Run the app:
```
dotnet run --project src/BuckHTTPCache/BuckHTTPCache.fsproj 
```

It is suggested to create default cache directory - 'src/BuckHTTPCache/cache' - for the application to be able to read / write it,
 because by default filesystem cache type is used.

After the application has started visit [http://localhost:5080](http://localhost:5080) in your preferred browser.
