module Lyckaro.ExampleDomains

open Bogus

let f = Faker()

type Person =
  { firstName: string
    lastName: string
    age: uint }
  
let generatePerson () =
  { firstName = f.Name.FirstName()
    lastName = f.Name.LastName()
    age = f.Random.Number(18, 60) |> uint }

type DogSpec =
  { owner: Person
    age: uint
    name: string }

type CatSpec = { age: uint; name: string }

type Pet =
  | Dog of DogSpec
  | Cat of CatSpec
  
let generatePet () =
  match f.Random.Bool() with
  | true ->
      Cat {
        age = f.Random.UInt(1u, 15u)
        name = f.Name.FirstName()
      }
  | false ->
      Dog {
        owner = generatePerson()
        age = f.Random.UInt(1u, 10u)
        name = f.Name.FirstName()
      }

type Family = { people: Person[]; pets: Pet[] }
