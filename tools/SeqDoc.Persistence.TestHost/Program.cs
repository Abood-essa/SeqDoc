using SeqDoc.Core.Identity;
using SeqDoc.Persistence.Sqlite;

if (args.Length != 2)
{
    return 2;
}

var result = await new SqliteProgramIndexStore(args[0]).ReadActiveAsync(
    new CompilationProfileId(args[1]),
    CancellationToken.None);
if (!result.IsSuccess || result.Value?.ActiveIndex is null)
{
    return 3;
}

var snapshot = result.Value.ActiveIndex.Snapshot;
Console.Write($"{snapshot.IndexFingerprint}|{snapshot.Projects.Length}|{snapshot.Documents.Length}|{snapshot.Methods.Length}|{snapshot.Diagnostics.Length}");
return 0;
