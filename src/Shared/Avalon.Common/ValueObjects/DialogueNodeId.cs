namespace Avalon.Common.ValueObjects;

public class DialogueNodeId : ValueObject<int>, IHideObjectMembers
{
    public DialogueNodeId(int value) : base(value) { }

    public static implicit operator DialogueNodeId(int value)
    {
        return new DialogueNodeId(value);
    }
}
