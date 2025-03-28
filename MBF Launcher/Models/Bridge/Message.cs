using System.Text.Json;
using System.Text.Json.Serialization;

namespace mbf_launcher.Models.Bridge
{
    public class MessagePayload
    {
        [JsonPropertyName("message")]
        public string message { get; set; }
    }

    public class Message
    {
        [JsonPropertyName("message_type")]
        public string MessageType { get; set; }

        public static Message? FromJson(string json) => JsonSerializer.Deserialize<Message>(json);
        public static Message<T>? FromJson<T>(string json) => JsonSerializer.Deserialize<Message<T>>(json);
    }

    public class Message<T> : Message
    {

        [JsonPropertyName("payload")]
        public T Payload { get; set; }
    }

    public class JsonPayload : MessagePayload { }
    public class JsonMessage : Message<JsonPayload> { }
    public class StandardMessage : Message<MessagePayload> { }
    public class ErrorMessage : Message<MessagePayload> { }
    public class UnknownJsonMessage : Message<JsonPayload>
    {
        public UnknownJsonMessage()
        {
            MessageType = "UnknownJsonMessage";
        }
    }
    public class UnknownMessage : Message<MessagePayload>
    {
        public UnknownMessage()
        {
            MessageType = "UnknownMessage";
        }
    }
}