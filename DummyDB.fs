module Lyckaro.DummyDB

let dummyDBLookup (lookupTime: int) (result: 'a) : unit -> 'a =
  fun () ->
    Async.RunSynchronously(
      async {
        do printfn $"db lookup of {result}"
        do! Async.Sleep(lookupTime)
        return result
      }
    )
