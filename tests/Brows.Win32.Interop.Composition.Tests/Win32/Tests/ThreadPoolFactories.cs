using Brows.Threading;

namespace Brows.Win32.Tests;

internal static class ThreadPoolFactories {
    internal static ISTAThreadPoolFactory Mock() {
        var threadPoolFactory = new Mock<ISTAThreadPoolFactory>();
        threadPoolFactory
            .Setup(factory => factory.Create(It.IsAny<string>(), It.IsAny<STAThreadPoolOptions>()))
            .Returns<string, STAThreadPoolOptions>((name, _) => new STAThreadPool(name));
        return threadPoolFactory.Object;
    }
}
