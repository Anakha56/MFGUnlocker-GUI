using MFGUnlockerGUI.Core;
using MFGUnlockerGUI.Tests.Fixtures;
using Xunit;

namespace MFGUnlockerGUI.Tests
{
    public class EngineDetectorTests
    {
        [Fact]
        public void Engine_Unreal()
        {
            using (var g = FakeGame.Create().Dir("Engine").File(@"Game\Binaries\Win64\Game-Win64-Shipping.exe", 100))
                Assert.Equal(Engine.Unreal, EngineDetector.Detect(g.Root));
        }

        [Fact]
        public void Engine_Unity()
        {
            using (var g = FakeGame.Create().File("Game.exe").File("UnityPlayer.dll").Dir("Game_Data"))
                Assert.Equal(Engine.Unity, EngineDetector.Detect(g.Root));
        }

        [Fact]
        public void Engine_RE()
        {
            using (var g = FakeGame.Create().File("game.exe").File("re_chunk_000.pak"))
                Assert.Equal(Engine.REEngine, EngineDetector.Detect(g.Root));
        }

        [Fact]
        public void Engine_RED()
        {
            using (var g = FakeGame.Create().File(@"bin\x64\REDprelauncher.exe"))
                Assert.Equal(Engine.REDengine, EngineDetector.Detect(g.Root));
        }

        [Fact]
        public void Engine_Unknown()
        {
            using (var g = FakeGame.Create().File("game.exe").File("data.bin"))
                Assert.Equal(Engine.Unknown, EngineDetector.Detect(g.Root));
        }
    }
}
