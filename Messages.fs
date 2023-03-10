module Lyckaro.Messages

open Lyckaro.ExampleDomains

type PersonMessage =
  | PersonDeleted of string
  | PersonAdded of string * Person
  | PersonUpdated of string * Person
  
type FamilyMessage =
  | FamilyAdded of string
