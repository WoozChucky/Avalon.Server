using Avalon.ChunkGen;

// Writes the generated chunk pieces into the world server's catalog (Maps/Chunks).
//
//   dotnet run --project tools/Avalon.ChunkGen -- forest
//   dotnet run --project tools/Avalon.ChunkGen -- forest --maps <Maps directory>
//
// Every piece is first written into a temporary copy of Maps/, read back through the World server's own catalog
// reader (ChunkCatalogSeeder.ReadCatalogAsync) and baked with its own ChunkLayoutNavmeshBuilder, each set piece with
// its members at their cells; only when all of that passes are the files copied into Maps/Chunks (ChunkGenCli).
// Committed output is checked against a fresh run by ForestPiecesShould, so edit ForestPieces.cs and rerun this rather
// than editing the files. chunk-pools.json and chunk-groups.json are edited by hand.

return await ChunkGenCli.RunAsync(args, ForestPieces.Singles, ForestPieces.Groups, Console.Out, Console.Error);
