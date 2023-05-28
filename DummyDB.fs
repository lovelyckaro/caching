module Lyckaro.DummyDB

open System
open Serilog

let dummyDBLookup (lookupTime: int) (generator: unit -> 'a) (key: string) : unit -> 'a =
  fun () -> Async.RunSynchronously(
    async {
      let res = generator ()
      Log.Debug("db lookup of {Key}", key)
      do! Async.Sleep(lookupTime)
      return res
    }
  )

let rnd = Random()

let dummyDBPost (latency: int) (_value: 'a) : string =
  async {
    do! Async.Sleep latency
    return rnd.Next().ToString()
  }
  |> Async.RunSynchronously

let dummyDBPut (latency: int) (_key: string) (_value: 'a) : unit =
  latency |> Async.Sleep |> Async.RunSynchronously

let dummyDBDelete (latency: int) (_key: string) : unit =
  latency |> Async.Sleep |> Async.RunSynchronously
