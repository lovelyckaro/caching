module Lyckaro.ExampleDomains

type Person =
  { firstName: string
    lastName: string
    age: uint }

type DogSpec =
  { owner: Person
    age: uint
    name: string }

type CatSpec = { age: uint; name: string }

type Pet =
  | Dog of DogSpec
  | Cat of CatSpec

type Family = { people: Person[]; pets: Pet[] }
