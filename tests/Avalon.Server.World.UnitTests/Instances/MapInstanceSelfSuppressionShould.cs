using Avalon.Common;
using Avalon.World.Instances;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Server.World.UnitTests.Instances;

public class MapInstanceSelfSuppressionShould
{
    [Fact]
    public void Strip_position_velocity_orientation_when_recipient_is_subject()
    {
        var subjectGuid = new ObjectGuid(ObjectType.Character, 7);
        var recipientGuid = new ObjectGuid(ObjectType.Character, 7);
        var fields = MapInstance.MaskSelfSuppression(GameEntityFields.CharacterUpdate, subjectGuid, recipientGuid);

        Assert.False(fields.HasFlag(GameEntityFields.Position));
        Assert.False(fields.HasFlag(GameEntityFields.Velocity));
        Assert.False(fields.HasFlag(GameEntityFields.Orientation));
        // Other unit fields preserved.
        Assert.True(fields.HasFlag(GameEntityFields.CurrentHealth));
    }

    [Fact]
    public void Preserve_full_bitmap_when_recipient_is_other()
    {
        var subjectGuid = new ObjectGuid(ObjectType.Character, 7);
        var recipientGuid = new ObjectGuid(ObjectType.Character, 8);
        var fields = MapInstance.MaskSelfSuppression(GameEntityFields.CharacterUpdate, subjectGuid, recipientGuid);

        Assert.Equal(GameEntityFields.CharacterUpdate, fields);
    }
}
