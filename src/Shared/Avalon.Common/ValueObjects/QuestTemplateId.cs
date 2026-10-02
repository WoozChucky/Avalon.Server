namespace Avalon.Common.ValueObjects;

public class QuestTemplateId : ValueObject<uint>, IHideObjectMembers
{
    public QuestTemplateId(uint value) : base(value) { }

    public static implicit operator QuestTemplateId(uint value) => new(value);
}
