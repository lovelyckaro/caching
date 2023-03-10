namespace Lyckaro

module RedisMessageBroker =
  open StackExchange.Redis
  open Newtonsoft.Json

  type MessageBroker(redisChannel: ISubscriber) =
    member this.publish(message: 'T) : unit =
      let channel = typeof<'T>.FullName
      redisChannel.Publish(channel, JsonConvert.SerializeObject message |> RedisValue)
      |> ignore

    
    member this.subscribe(handler: 'T -> unit) : unit =
      let channel = typeof<'T>.FullName
      let messages = redisChannel.Subscribe(channel)
      let fullHandler (message : ChannelMessage) : unit =
        message.Message.ToString()
        |> JsonConvert.DeserializeObject<'T>
        |> handler
      in
      messages.OnMessage fullHandler
  

(*
Variant 1:
subscribe and publish differentiate different messages with the type of the message
Just one messagebroker

MessageBroker.publish<'T> : 'T -> unit

MessageBroker.subscribe<'T> : ('T -> unit) -> unit

Example usage:

type PersonUpdateMessage = UpdatePerson of UUID * Person

let peopleUpdateHandler id newPerson =
  peopleCache.invalidate id
  database.write id newPerson
  UpdatePerson id newPerson |> Messages.publish

type FamilyController =
  ...
  do
    MessageBroker.subscribe<PersonUpdate> (
      fun UpdatePerson id person -> this.invalidateFamiliesWithPerson id
    )
    
    MessageBroker.subscribe<PetUpdated> (
      fun UpdatePet id newName -> 
        familyCache.invalidateAll()
        MessageBroker.publish<FamilyUpdated> 
    )
    
---------------

Variant 2:
One MessageBroker object per type of message
Kind of like the event system. If we want functions to be able to subscribe to peopleUpdate messages we create a
messagebroker for that type of message 

let peopleMessages = new MessageBroker<PersonUpdate>()

let peopleUpdateHandler id newPerson =
  ...
  UpdatePerson id newPerson |> peopleMessages.publish
  
type FamilyController () =
  ...
  do
    peopleMessages.subscribe (
      fun UpdatePerson id newPerson -> ...
    )
    
-------------

Variant 3:
One (potentielly very large type for all messages)

type Message =
  | UpdatePerson of ...
  | NewPerson of ...
  | NewFamily of ...
  | UpdatedFieldInFamily of ...
  | PetDied of ...
  | FamilyMerge of ...
  | AllPeopleChangedYouShouldJustRecalculateEverything
  
let peopleUpdateHandler id newPerson =
  ...
  UpdatePerson id newPerson |> Messages.publish
  
type FamilyController () =
  ...
  do
    Messages.subscribe (
      function
      | UpdatePerson id newPerson -> ...
      | NewFamily ... -> ...
      | _ -> ()
    )




*)
