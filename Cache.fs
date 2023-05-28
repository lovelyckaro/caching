namespace Lyckaro

[<AutoOpen>]
module CacheCommon =
  type DecoratedCacheResponse<'T> =
    { resultValue: Option<'T>
      cacheResponseTime: int64
      totalResponseTime: int64
      wasHit: bool
      lookupTime: Option<int64> }

  type ICache =
    abstract member get: string -> Option<'T>
    abstract member set: string -> 'T -> unit
    abstract member getOrAdd: string -> (unit -> 'T) -> Option<'T>
    abstract member remove: string -> unit
    abstract member getDecorated: string -> DecoratedCacheResponse<'T>
    abstract member setDecorated: string -> 'T -> DecoratedCacheResponse<unit>
    abstract member getOrAddDecorated: string -> (unit -> 'T) -> DecoratedCacheResponse<'T>
    abstract member removeDecorated: string -> DecoratedCacheResponse<unit>



module RedisCache =
  open StackExchange.Redis
  open Newtonsoft.Json
  open System.Diagnostics

  type Cache(domain: string, db: IDatabase) =
    member this.domain = domain

    member private this.getKey(key: string) : RedisKey = new RedisKey(domain + ":" + key)

    interface ICache with
      member this.get<'T>(key: string) : Option<'T> =
        let raw = db.StringGet(this.getKey (key)) in

        if raw.HasValue then
          Some(raw.ToString() |> JsonConvert.DeserializeObject<'T>)
        else
          None

      member this.getDecorated<'T>(key: string) : DecoratedCacheResponse<'T> =
        let stopwatch = Stopwatch()
        stopwatch.Start()
        let cachedValue = (this :> ICache).get key
        stopwatch.Stop()
        let time = stopwatch.ElapsedMilliseconds in

        { resultValue = cachedValue
          cacheResponseTime = time
          totalResponseTime = time
          wasHit = cachedValue.IsSome
          lookupTime = None }

      member this.set<'T> (key: string) (value: 'T) : unit =
        db.StringSet(this.getKey (key), JsonConvert.SerializeObject value) |> ignore

      member this.setDecorated (key: string) (value: 'T) : DecoratedCacheResponse<unit> =
        let stopwatch = Stopwatch()
        stopwatch.Start()
        (this :> ICache).set key value
        stopwatch.Stop()
        let time = stopwatch.ElapsedMilliseconds in

        { resultValue = Some()
          cacheResponseTime = time
          totalResponseTime = time
          wasHit = true
          lookupTime = None }

      member this.getOrAdd<'T> (key: string) (expensiveLookupFunction: unit -> 'T) : Option<'T> =
        match (this :> ICache).get key with
        | Some value -> Some value
        | None ->
          let value = expensiveLookupFunction () in
          (this :> ICache).set key value
          Some value

      member this.getOrAddDecorated (key: string) (expensiveLookupFunction: unit -> 'T) : DecoratedCacheResponse<'T> =
        let stopwatchTotal = Stopwatch()
        let stopwatchCache = Stopwatch()
        stopwatchTotal.Start()
        stopwatchCache.Start()

        let (value, wasHit) =
          match (this :> ICache).get key with
          | Some value ->
            stopwatchCache.Stop()
            stopwatchTotal.Stop()
            (Some value, true)
          | None ->
            stopwatchCache.Stop()
            let value = expensiveLookupFunction () in
            (this :> ICache).set key value
            stopwatchTotal.Stop()
            (Some value, false)

        let cacheTime = stopwatchCache.ElapsedMilliseconds
        let totalTime = stopwatchTotal.ElapsedMilliseconds in

        { resultValue = value
          cacheResponseTime = cacheTime
          totalResponseTime = totalTime
          wasHit = wasHit
          lookupTime = if wasHit then None else Some(totalTime - cacheTime) }

      member this.remove(key: string) : unit =
        db.StringGetDelete(this.getKey key) |> ignore

      member this.removeDecorated(key: string) : DecoratedCacheResponse<unit> =
        let stopwatch = Stopwatch()
        stopwatch.Start()
        let res = db.StringGetDelete(this.getKey key)
        stopwatch.Stop()
        let time = stopwatch.ElapsedMilliseconds
        let wasHit = res.HasValue in

        { resultValue = if wasHit then Some() else None
          cacheResponseTime = time
          totalResponseTime = time
          wasHit = wasHit
          lookupTime = None }

module InMemoryCache =
  open System.Collections.Concurrent
  open Lyckaro.RedisMessageBroker
  open System.Diagnostics

  /// Invalidation message type. First string is domain, second key to invalidate
  /// These are automatically sent to other caches on write
  type CacheInvalidationMessage = Invalidate of string * string

  type Cache(domain: string, messagebroker: MessageBroker) =
    let dict: ConcurrentDictionary<string, obj> =
      new ConcurrentDictionary<string, obj>()

    do
      messagebroker.subscribe<CacheInvalidationMessage>
      <| fun (Invalidate (invalidateDomain, key)) ->
           if invalidateDomain = domain then
             dict.TryRemove key |> ignore
           else
             ()

    interface ICache with

      member this.getOrAdd (key: string) (expensiveCalculation: unit -> 'T) : option<'T> =
        let v = dict.GetOrAdd(key, (fun _ -> expensiveCalculation () :> obj)) in

        match v with
        | :? 'T as t -> Some t
        | _ -> None

      member this.getOrAddDecorated (key: string) (expensiveCalculation: unit -> 'T) : DecoratedCacheResponse<'T> =
        let stopwatchCache = Stopwatch()
        let stopwatchTotal = Stopwatch()
        stopwatchCache.Start()
        stopwatchTotal.Start()
        let wasHit = dict.ContainsKey key
        let v =
          dict.GetOrAdd(
            key,
            (fun _ ->
              stopwatchCache.Stop()
              let value = expensiveCalculation () :> obj
              stopwatchTotal.Stop()
              value)
          )

        let response =
          match v with
          | :? 'T as t -> Some t
          | _ -> None

        let totalTime = stopwatchTotal.ElapsedMilliseconds
        let cacheTime = stopwatchCache.ElapsedMilliseconds
        in

        { resultValue = response
          cacheResponseTime = cacheTime
          totalResponseTime = totalTime
          wasHit = wasHit
          lookupTime = if wasHit then None else Some(totalTime - cacheTime) }

      member this.get(key: string) : Option<'T> =
        match dict.TryGetValue key with
        | (true, v) ->
          match v with
          | :? 'T as t -> Some t
          | _ -> None
        | (false, _) -> None

      member this.getDecorated(key: string) : DecoratedCacheResponse<'T> =
        let stopwatch = Stopwatch()
        stopwatch.Start()

        let value =
          match dict.TryGetValue key with
          | (true, v) ->
            match v with
            | :? 'T as t -> Some t
            | _ -> None
          | (false, _) -> None

        stopwatch.Stop()
        let time = stopwatch.ElapsedMilliseconds in

        { resultValue = value
          cacheResponseTime = time
          totalResponseTime = time
          wasHit = value.IsSome
          lookupTime = None }

      member this.remove(key: string) : unit =
        dict.TryRemove key |> ignore
        this.invalidate key

      member this.removeDecorated(key: string) : DecoratedCacheResponse<unit> =
        let stopwatch = Stopwatch()
        stopwatch.Start()
        let wasHit = dict.ContainsKey key
        dict.TryRemove key |> ignore
        this.invalidate key
        stopwatch.Stop()
        let time = stopwatch.ElapsedMilliseconds in

        { resultValue = Some()
          cacheResponseTime = time
          totalResponseTime = time
          wasHit = wasHit
          lookupTime = None }

      // Note, only updates this instance of the cached value. Other instances need to be updated by themselves.
      // A typical use case would look like:
      // update db value
      // cache.remove key
      // cache.set key value
      member this.set (key: string) (value: 'T) : unit =
        dict.AddOrUpdate(key, value :> obj, (fun k prevValue -> value :> obj)) |> ignore

      member this.setDecorated (key: string) (value: 'T) : DecoratedCacheResponse<unit> =
        let stopwatch = Stopwatch()
        stopwatch.Start()
        (this :> ICache).set key value
        stopwatch.Stop()
        let time = stopwatch.ElapsedMilliseconds
        in { resultValue = Some ()
             cacheResponseTime = time
             totalResponseTime = time
             wasHit = true
             lookupTime = None }

    member private this.invalidate(key: string) : unit =
      messagebroker.publish (Invalidate(domain, key))

    member this.keys: string seq = dict.Keys

    member this.isEmpty: bool = dict.IsEmpty
