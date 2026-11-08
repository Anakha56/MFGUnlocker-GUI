using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class FrameGenDetectorTests
    {
        [Fact]
        public void FrameGen_FindsDeepStreamline()
        {
            const string rel = @"Engine\Plugins\Runtime\Nvidia\StreamlineCore\Binaries\ThirdParty\Win64\sl.dlss_g.dll";
            using (var g = FakeGame.Create().File(rel).File("nvngx_dlssg.dll").File("other.dll"))
            {
                var found = FrameGenDetector.Find(g.Root);
                Assert.Contains(rel, found);
                Assert.Contains("nvngx_dlssg.dll", found);
                Assert.Equal(2, found.Count);
            }
        }

        [Fact]
        public void FrameGen_EmptyGame_Empty()
        {
            using (var g = FakeGame.Create())
                Assert.Empty(FrameGenDetector.Find(g.Root));
        }
    }
}
