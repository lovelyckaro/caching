open StackExchange.Redis

type Cache(domain: string, db: IDatabase) =
  member this.domain = domain
  member private this.getKey(key: string) : RedisKey = new RedisKey(domain + ":" + key)
  member this.get(key: string) =
    let raw = db.StringGet(this.getKey (key))
    in if raw.HasValue
      then Some raw
      else None

  member this.set (key: string) (value: string) : unit =
    db.StringSet(this.getKey (key), value) |> ignore

  member this.getOrSet (key: string) (expensiveLookupFunction: Lazy<string>) =
    match this.get key with
    | Some value -> value
    | None -> let value = expensiveLookupFunction.Force()
              let _ = this.set key value
              in new RedisValue(value)
  

/// A function to use as a dummy lookup to the database. Whenever the output is forced, thread will wait for lookupTime
/// ms, and then return result.
let dummyDBLookup (lookupTime: int) result =
  lazy
    Async.RunSynchronously(
      async {
        do printfn $"db lookup of {result}"
        do! Async.Sleep(lookupTime)
        return result
      }
    )

[<EntryPoint>]
let main _args =
  // connect to redis
  let redis = ConnectionMultiplexer.Connect("localhost").GetDatabase()
  // Create caches, all of which share the same connection to redis
  let peopleCache = new Cache("people", redis)
  let animalCache = new Cache("animal", redis)
  let dblookup = dummyDBLookup 500
  let love = peopleCache.getOrSet "love" (dblookup "Love Lyckaro")
  let hund = animalCache.getOrSet "hund" (dblookup "En supersöt hund")
  let test = peopleCache.get "hund"
  printfn $"{love}"
  printfn $"{hund}"
  printfn $"test = {test}"
  0
