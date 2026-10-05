using BasicLang.Net;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 7d review: <c>NetTypeResolver.CreateShared</c> keeps ONE resolver per reference PATH SET. A rebuilt assembly (a
/// changed stamp) must REPLACE the entry — a fresh resolver, the old one dropped — never add a second one beside it, or
/// the cache grows by one resolver per rebuild for the life of an IDE process.
/// </summary>
[TestFixture]
[NonParallelizable]
public class NetTypeResolverSharedCacheTests
{
    [Test]
    public void AChangedStamp_ReplacesTheEntry_RatherThanAddingOne()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-rescache-" + Path.GetRandomFileName())).FullName;
        try
        {
            var path = Path.Combine(dir, "Probe.dll");
            File.Copy(typeof(NUnit.Framework.Assert).Assembly.Location, path);
            var closure = new[] { path };

            var first = NetTypeResolver.CreateShared(closure);
            var count = NetTypeResolver.SharedResolverCountForTest;
            Assert.That(NetTypeResolver.CreateShared(closure), Is.SameAs(first), "an unchanged closure is served from the cache");

            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
            var second = NetTypeResolver.CreateShared(closure);

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.Not.SameAs(first), "a rebuilt assembly must not be served stale");
                Assert.That(NetTypeResolver.SharedResolverCountForTest, Is.EqualTo(count), "the entry is REPLACED, not added beside");
            });
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
