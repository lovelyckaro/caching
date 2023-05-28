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
