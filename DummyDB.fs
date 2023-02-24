module Lyckaro.DummyDB

let dummyDBLookup (lookupTime: int) (result: 'a) : Lazy<'a> =
  lazy
    Async.RunSynchronously(
      async {
        do printfn $"db lookup of {result}"
        do! Async.Sleep(lookupTime)
        return result
      }
    )
