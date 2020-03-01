module Datastore

open System.Collections.Generic
open System.IO
open System.Security.Permissions
open Giraffe.GoodRead

let UnblockViaNewThread f =
    async {
        do! Async.SwitchToNewThread()
        let res = f()
        do! Async.SwitchToThreadPool()
        return res
    }

type CacheEntry =
    { Data: byte [] }

type CacheError =
    | NotFound
    | WriteError of string
    | ReadError of string

type ICacheDatastore =
    abstract getEntry: string -> Async<Result<CacheEntry, CacheError>>
    abstract setEntry: string [] * CacheEntry -> Async<Result<unit, CacheError>>
    abstract summary: unit -> Async<int * int>

type InMemoryCache() =
    let ents = Dictionary<string [], CacheEntry>()
    interface ICacheDatastore with
        member this.getEntry(key: string) =
            ents.Keys
            |> Seq.tryFind (fun k -> k |> Array.contains key)
            |> function
                | Some x -> async {return Result.Ok (ents.Item x)}
                | None -> async {return Result.Error CacheError.NotFound}

        member this.setEntry(key: string [], entry: CacheEntry) =
            async {
                ents.Add(key, entry)
                return Result.Ok()
            }
        member this.summary() =
            async {
                return (ents.Keys |> Seq.map (fun x -> x.Length) |> Seq.sum,
                        ents.Values.Count)
            }

type FileCache() =

    let isDirectoryWritable path: Result<unit, exn> =
        try
            if (not (Directory.Exists(path))) then Directory.CreateDirectory(path) |> ignore
            let perm =
                System.Security.Permissions.FileIOPermission
                    (FileIOPermissionAccess.Write, Path.Combine(path, "test.txt"))
            perm.Demand()
            Result.Ok()
        with e -> Result.Error e
    let writeEntryToFileAsync data key  =
        async {
            let filePath = Path.Combine(Settings.cacheDirectory, key)
            do! File.WriteAllBytesAsync(filePath, data) |> Async.AwaitTask
        }
        
    interface ICacheDatastore with

        member this.summary() = UnblockViaNewThread (fun () -> Directory.EnumerateFiles(Settings.cacheDirectory) |> Seq.length, 0)

        member this.getEntry (key: string) =
            async {
                try
                    let files = Directory.GetFiles(Settings.cacheDirectory, "*" + key + "*")
                    if (Array.length files >= 1) then
                        let! data = files
                                    |> Array.head
                                    |> File.ReadAllBytesAsync
                                    |> Async.AwaitTask
                        return Result.Ok
                                   { Data = data }
                    else
                        return CacheError.NotFound |> Result.Error
                with e ->
                    return e.Message
                           |> CacheError.ReadError
                           |> Result.Error
            }
        
        member this.setEntry (key: string [], entry: CacheEntry) =
            async {
                try
                    match isDirectoryWritable Settings.cacheDirectory with
                    | Ok _ ->
                        let writeThisEntryToFileAsync = writeEntryToFileAsync entry.Data
                        let! _ = key |> Seq.map writeThisEntryToFileAsync |> Async.Parallel
                        return Result.Ok()
                    | Error e -> return Result.Error(CacheError.WriteError e.Message)

                with e ->
                    return e.Message
                           |> CacheError.WriteError
                           |> Result.Error
            }


type SelectedCacheType =
    | Memory of InMemoryCache
    | File of FileCache
    
let cacheTypeOfString (s:string) :Result<SelectedCacheType,string> =
    let allowedTypes = seq {"memory";"file"}
    match s.ToLower().Trim() with
    | "memory" -> InMemoryCache() |> Memory |> Result.Ok
    | "file" -> FileCache() |> SelectedCacheType.File |> Result.Ok
    | _ -> (sprintf "CACHE_TYPE should be in %A, not %s" allowedTypes (s.ToLower().Trim())) |> Result.Error
    
let cacheTypeOfStringToExn (s:string) :SelectedCacheType =
    match cacheTypeOfString s with
    | Result.Ok x -> x
    | Result.Error y -> failwith y