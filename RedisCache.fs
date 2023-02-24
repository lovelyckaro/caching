namespace Lyckaro

open StackExchange.Redis
open Newtonsoft.Json

module RedisCache =
  type Cache(domain: string, db: IDatabase) =
    member this.domain = domain
    member private this.getKey(key: string) : RedisKey = new RedisKey(domain + ":" + key)

    member this.get<'T>(key: string) : Option<'T> =
      let raw = db.StringGet(this.getKey (key)) in

      if raw.HasValue then
        Some(raw.ToString() |> JsonConvert.DeserializeObject<'T>)
      else
        None

    member this.set<'T> (key: string) (value: 'T) : unit =
      db.StringSet(this.getKey (key), JsonConvert.SerializeObject value) |> ignore

    member this.getOrSet<'T> (key: string) (expensiveLookupFunction: Lazy<'T>) : 'T =
      match this.get key with
      | Some value -> value
      | None ->
        let value = expensiveLookupFunction.Force()
        let _ = this.set key value in
        value
