module Lyckaro.Main

open StackExchange.Redis
open Lyckaro.DummyDB
open Lyckaro.RedisCache

[<EntryPoint>]
let main args =
  // connect to redis
  let redis = ConnectionMultiplexer.Connect("localhost").GetDatabase()
  // Create caches, all of which share the same connection to redis
  let peopleCache = new Cache("people", redis)
  let animalCache = new Cache("animal", redis)
  let familyCache = new Cache("family", redis)
  let dblookup = dummyDBLookup 500

  let love =
    { firstName = "Love"
      lastName = "Lyckaro"
      age = 23 }
  let emma =
    { firstName = "Emma"
      lastName = "Ivarsson"
      age = 23 }
  let euler = Dog (emma, "Euler")

  let family =
    { people = [ love; emma ]
      pets = [ euler ] }
  peopleCache.set "love" love
  peopleCache.set "emma" emma
  animalCache.set "euler" euler
  familyCache.set "loveEmma" family
  printfn $"""{familyCache.get<Family> "loveEmma" }"""
  0