module Settings
    open dotenv.net
    open System
    DotEnv.Config(false)
    let private readEnv name =
        match System.Environment.GetEnvironmentVariable(name) with
            | null -> None
            | x -> Some x

    let inline internal readEnvDefault name f ``default`` =
        readEnv name |> Option.map f |> Option.defaultValue ``default``
    let port = readEnvDefault "PORT" Int32.Parse 5080
    
    let maxRequestBodySize = readEnvDefault "MAX_REQUEST_BODY_SIZE" Int64.Parse 737280000L
    let maxRequestBufferSize = readEnvDefault "MAX_REQUEST_BUFFER_SIZE" Int64.Parse 30000000L
    let maxConcurrentConnections = readEnvDefault "MAX_CONCURRENT_CONNECTIONS" Int64.Parse 3000L
    let host = readEnvDefault "HOST" id "127.0.0.1"
    let cacheDirectory = readEnvDefault "CACHE_DIR" id (System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "cache"))
