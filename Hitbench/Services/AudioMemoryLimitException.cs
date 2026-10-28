using System.IO;

namespace Hitbench.Services;

public sealed class AudioMemoryLimitException(string message) : IOException(message);
