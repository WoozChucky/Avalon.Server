namespace Avalon.Database.Character.Repositories;

/// <summary>
/// A save's transaction finished its work, and then the commit failed or its acknowledgement was lost (#1175): the
/// write may or may not have committed. Thrown by <see cref="ICharacterSaveRepository.WriteAsync" /> and
/// <see cref="ICharacterSaveRepository.WriteTradeAsync" /> only for an exception from the commit or after it; any
/// exception before that point rolled the transaction back and propagates unchanged, meaning nothing was written. A
/// trade settles it with <see cref="ICharacterSaveRepository.TradeRecordExistsAsync" />.
/// </summary>
public sealed class SaveOutcomeUnknownException(Exception inner)
    : Exception("The save's transaction finished its work, but whether it committed is unknown.", inner);
