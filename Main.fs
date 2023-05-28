module Lyckaro.Main

open Serilog.Sinks.Graylog
open Serilog.Sinks.Graylog.Core.Transport
open StackExchange.Redis
open Lyckaro.DummyDB
open Lyckaro.RedisCache
open Lyckaro.ExampleDomains
open Lyckaro.RedisMessageBroker
open Lyckaro.Messages
open Serilog
open System
open Suave
open Suave.Filters
open Suave.Utils.Collections
open Newtonsoft.Json

let (>=>) = compose

let logCacheResult (method: String) (domain: String) (key: string) (res: DecoratedCacheResponse<'T>) : unit =
  Log.Information(
    "{method}, /{domain}/{key}, {cacheResponseTime} ms, {totalResponseTime} ms, {wasHit}, {lookupTime} ms",
    method,
    domain,
    key,
    res.cacheResponseTime,
    res.totalResponseTime,
    res.wasHit,
    res.lookupTime
  )

let handlePut (domain: string) (cache: ICache) (putFunction: string -> 'T -> unit) : WebPart =
  pathScan (PrintfFormat<_, _, _, _, string>("/" + domain + "/%s")) (fun key ->
    PUT
    >=> Writers.setMimeType "application/json; charset=utf-8"
    >=> request (fun req ->
      let cacheResult = cache.removeDecorated key
      logCacheResult "PUT" domain key cacheResult

      req.rawForm
      |> UTF8.toString
      |> JsonConvert.DeserializeObject<'T>
      |> putFunction key

      Successful.OK(JsonConvert.SerializeObject cacheResult)))

let handleGet (domain: string) (cache: ICache) (expensiveGet: string -> unit -> 'T) : WebPart =
  pathScan (PrintfFormat<_, _, _, _, string>("/" + domain + "/%s")) (fun key ->
    let value = cache.getOrAddDecorated<'T> key (expensiveGet key) in
    logCacheResult "GET" domain key value

    GET
    >=> Successful.OK(JsonConvert.SerializeObject value)
    >=> Writers.setMimeType "application/json; charset=utf-8")

let handlePost (domain: string) (cache: ICache) (postFunction: 'T -> string) : WebPart =
  path ("/" + domain)
  >=> POST
  >=> Writers.setMimeType "application/json; charset=utf-8"
  >=> request (fun req ->
    req.rawForm
    |> UTF8.toString
    |> JsonConvert.DeserializeObject<'T>
    |> postFunction
    |> Successful.OK)

let handleDelete (domain: string) (cache: ICache) (deleteFunction: string -> unit) : WebPart =
  pathScan (PrintfFormat<_, _, _, _, string>("/" + domain + "/%s")) (fun key ->
    deleteFunction key
    let cacheResult = cache.removeDecorated key in
    logCacheResult "DELETE" domain key cacheResult

    DELETE
    >=> Writers.setMimeType "application/json; charset=utf-8"
    >=> Successful.OK(JsonConvert.SerializeObject cacheResult))

let mkApp
  (domain: string)
  (cache: ICache)
  (expensiveLookup: string -> unit -> 'T)
  (postFunction: 'T -> string)
  (putFunction: string -> 'T -> unit)
  (removeFunction: string -> unit)
  : WebPart =
  choose
    [ handleGet domain cache expensiveLookup
      handlePut domain cache putFunction
      handlePost domain cache postFunction
      handleDelete domain cache removeFunction ]

[<EntryPoint>]
let main _args =
  // connect to redis
  let connection = ConnectionMultiplexer.Connect("localhost")

  let redis = connection.GetDatabase()

  let messages = MessageBroker(connection.GetSubscriber())

  let graylogOptions = GraylogSinkOptions()
  graylogOptions.HostnameOrAddress <- "localhost"
  graylogOptions.Port <- 12201
  graylogOptions.TransportType <- TransportType.Udp

  printfn $"{graylogOptions.HostnameOrAddress}"

  let log =
    LoggerConfiguration()
      .WriteTo.Console()
      .WriteTo.Graylog(graylogOptions)
      .CreateLogger()

  Log.Logger <- log

  // let peopleCache = Cache("people", redis)
  // let petCache = Cache("pets", redis)
  // let familyCache = Cache("families", redis)
  let peopleCache = Lyckaro.InMemoryCache.Cache("people", messages)
  let petCache = Lyckaro.InMemoryCache.Cache("pets", messages)
  let familyCache = Lyckaro.InMemoryCache.Cache("families", messages)

  messages.subscribe<PersonMessage>
  <| function
    | PersonAdded (key, newPerson) -> Log.Information("New Person {Key} with value {Value}", key, newPerson)
    | PersonDeleted key -> Log.Information("Deleted Person {Key}", key)
    | PersonUpdated (key, newPerson) -> Log.Information("Updated Person {Key} with new value {Value}", key, newPerson)

  messages.subscribe<FamilyMessage>
  <| function
    | FamilyAdded key -> Log.Information("New Family {Key}", key)

  let dbLatency = 10 // ms

  let app =
    choose
      [ mkApp
          "user"
          peopleCache
          (dummyDBLookup dbLatency generatePerson)
          (dummyDBPost dbLatency)
          (dummyDBPut dbLatency)
          (dummyDBDelete dbLatency)
        mkApp
          "pet"
          petCache
          (dummyDBLookup dbLatency generatePet)
          (dummyDBPost dbLatency)
          (dummyDBPut dbLatency)
          (dummyDBDelete dbLatency) ]

  startWebServer defaultConfig app

  0
