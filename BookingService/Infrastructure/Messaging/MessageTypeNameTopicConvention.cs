using Rebus.Serialization;
using Rebus.Topic;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Имя топика = имя типа сообщения на проводе (с учётом CatalogMessageTypeNames).
/// Стандартная конвенция Rebus строит топик из .NET-типа и не учитывает UseCustomMessageTypeNames.
/// </summary>
public sealed class MessageTypeNameTopicConvention(IMessageTypeNameConvention messageTypeNames) : ITopicNameConvention
{
    public string GetTopic(Type eventType) => messageTypeNames.GetTypeName(eventType);
}
