namespace Avalon.Common.ValueObjects;

public class DialogueOptionId : ValueObject<int>, IHideObjectMembers
{
    public DialogueOptionId(int value) : base(value) { }

    public static implicit operator DialogueOptionId(int value)
    {
        return new DialogueOptionId(value);
    }
}
