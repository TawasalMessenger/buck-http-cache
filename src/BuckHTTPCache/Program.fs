module BuckHTTPCache.App

open System
open System.Buffers
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Giraffe
open FSharp.Control.Tasks.V2.ContextInsensitive
open Microsoft.AspNetCore.Server.Kestrel.Core
open Giraffe.GoodRead
open Datastore

// ---------------------------------
// Helpers
// ---------------------------------
module Settings =
    let cacheMode = Settings.readEnvDefault "CACHE_TYPE" cacheTypeOfStringToExn (cacheTypeOfStringToExn "file")
    
let finishEarly : HttpFunc = Some >> System.Threading.Tasks.Task.FromResult

let inline bigEndianReverse (xs: byte[]): byte[] =
    if not BitConverter.IsLittleEndian then xs else xs |> Array.rev
    
// ---------------------------------
// Web app
// ---------------------------------
let indexHandler = redirectTo true "/artifacts/summary"


let artifactSummaryHandler (dataStore:ICacheDatastore) =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let! s = dataStore.summary()
            return! text (sprintf "Storing %i keys for %i values" (fst s) (snd s)) next ctx
        }
        
let getArtifactHandler (dataStore:ICacheDatastore) (key: string) =
     fun (next : HttpFunc) (ctx : HttpContext) ->
         task {
             let logger = ctx.GetLogger("GETHandler")
             let! res = dataStore.getEntry key
             match res with
              |Result.Ok data ->
                  logger.LogInformation (sprintf "Key %s - cache HIT" key)
                  ctx.SetStatusCode StatusCodes.Status200OK
                  return! setBody data.Data next ctx
              |Result.Error e ->
                  match e with
                  |CacheError.NotFound ->
                      logger.LogInformation (sprintf "Key %s - cache MISS" key)
                      ctx.SetStatusCode StatusCodes.Status404NotFound
                      return! setBodyFromString "Not found" next ctx
                  |_ ->
                      logger.LogCritical (sprintf "Key %s - cache ERROR - %s" key (e.ToString()))
                      ctx.SetStatusCode StatusCodes.Status503ServiceUnavailable
                      return! setBodyFromString (e.ToString()) next ctx
         }

let putArtifactHandler (dataStore:ICacheDatastore) =
    fun (next : HttpFunc) (ctx : HttpContext) ->
        task {
            let logger = ctx.GetLogger("PUTHandler")
            let ms = new MemoryStream()
            do! ctx.Request.Body.CopyToAsync(ms) |> Async.AwaitTask
            let byteArray = ms.ToArray()
            ms.Dispose()
            let buffer = ReadOnlySequence<byte>(byteArray)
            let! response =
                task {
                    try
                        let mutable curPos = 0
                        let firstFourBytes = buffer.Slice(curPos, 4).ToArray()
                        let nKeys = firstFourBytes |> bigEndianReverse |> fun x -> BitConverter.ToInt32(x,0)
                        curPos <- 4
                        let keys = ResizeArray<string>()
                        
                        for i = 1 to nKeys do
                            let l = buffer.Slice(curPos, 2).ToArray() |> bigEndianReverse |> fun x -> BitConverter.ToUInt16(x,0)
                            curPos <- curPos + 2
                            let s = buffer.Slice(curPos, int32 l).ToArray() |> fun y -> System.Text.Encoding.UTF8.GetString(y)
                            curPos <- curPos + int32 l
                            keys.Add s
                        let buckData = buffer.Slice(curPos, buffer.End).ToArray()
                        let keysJoined = (String.Join(",",keys.ToArray()))
                        logger.LogInformation (sprintf "Finished parsing for keys %s" keysJoined)
                        
                        let! setRes = dataStore.setEntry(keys.ToArray(),{Data = buckData})
                        logger.LogInformation (sprintf "Populated cache for %s successfully" keysJoined)
                        return setRes
                    with
                        |e -> return Result.Error (CacheError.ReadError e.Message)
                }
            ctx.SetContentType "application/octet-stream"
            match response with
            | Result.Ok _ ->  ctx.SetStatusCode StatusCodes.Status202Accepted
                              ctx.SetContentType "application/octet-stream"
            | Result.Error e ->
                ctx.SetStatusCode StatusCodes.Status406NotAcceptable
                logger.LogCritical (sprintf "PUT cache ERROR - %s" (e.ToString()))
            
            return! finishEarly ctx
        }
        
let webApp =
    choose [
        GET >=>
            choose [
                route "/" >=> indexHandler
                routef "/artifacts/key/%s" (fun key ->
                    Require.services<ICacheDatastore>(fun store -> getArtifactHandler store key)                    
                )
                route "/artifacts/summary" >=> Require.services<ICacheDatastore> (fun store -> artifactSummaryHandler store)
            ]
        PUT >=>
            choose [
                route "/artifacts/key" >=> Require.services<ICacheDatastore> (fun store -> putArtifactHandler store)
            ]
        setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder : CorsPolicyBuilder) =
    builder.WithOrigins("http://localhost:8080")
           .AllowAnyMethod()
           .AllowAnyHeader()
           |> ignore

let configureApp (app : IApplicationBuilder) =
    app.UseGiraffeErrorHandler(errorHandler)
        //.UseHttpsRedirection()
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseGiraffe(webApp)
let configureKestrel (options: KestrelServerOptions) =
    options.Listen(System.Net.IPAddress.Parse(Settings.host), Settings.port)
    options.Limits.MaxRequestBodySize <- Some(Settings.maxRequestBodySize) |> Option.toNullable 
    options.Limits.MaxConcurrentConnections <- Some(Settings.maxConcurrentConnections) |> Option.toNullable
    options.Limits.MaxRequestBufferSize <-  Some(Settings.maxRequestBufferSize) |> Option.toNullable
    
let configureServices (services : IServiceCollection) =
    match Settings.cacheMode with
    | File f -> services.AddSingleton<ICacheDatastore>(f) |> ignore
    | Memory m -> services.AddSingleton<ICacheDatastore>(m) |> ignore
    services.AddCors()    |> ignore
    services.AddGiraffe() |> ignore


let configureLogging (builder : ILoggingBuilder) =
    builder
           .AddFilter(fun l -> [|LogLevel.Information;LogLevel.Error;LogLevel.Critical|] |> Array.contains l)
           .AddConsole()
           .AddDebug() |> ignore
           

[<EntryPoint>]
let main _ =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")
    WebHostBuilder()
        .UseKestrel(Action<KestrelServerOptions> configureKestrel)
        .UseContentRoot(contentRoot)
        .UseIISIntegration()
        .UseWebRoot(webRoot)
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .ConfigureLogging(configureLogging)
        .Build()
        .Run()
    0