using Horyzont.Core.Intents;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>
/// The reject reasons of K6-30 by number. The enum members are added with the protocol (step B2), because they change the golden values of
/// <c>RejectReason</c>; until then the tests name them here.
/// </summary>
internal static class FunctionalReasons
{
    /// <summary><c>RejectReason.NotOwner</c> (54): the block or its container belongs to another player.</summary>
    public const RejectReason NotOwner = (RejectReason)54;

    /// <summary><c>RejectReason.ContainerNotEmpty</c> (57): the container of the block still holds items.</summary>
    public const RejectReason ContainerNotEmpty = (RejectReason)57;
}
