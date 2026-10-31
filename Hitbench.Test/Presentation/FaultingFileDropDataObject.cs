using System.Windows;

namespace Hitbench.Test.Presentation;

public sealed class FaultingFileDropDataObject(IDataObject inner, bool failFormatRead) : IDataObject
{
    public object GetData(string format, bool autoConvert) => throw new IOException("drop data failed");
    public object GetData(string format) => GetData(format, true);
    public object GetData(Type format) => GetData(format.FullName!);
    public bool GetDataPresent(string format, bool autoConvert) => failFormatRead ? throw new IOException("drop format failed") : inner.GetDataPresent(format, autoConvert);
    public bool GetDataPresent(string format) => GetDataPresent(format, true);
    public bool GetDataPresent(Type format) => GetDataPresent(format.FullName!);
    public string[] GetFormats(bool autoConvert) => inner.GetFormats(autoConvert);
    public string[] GetFormats() => inner.GetFormats();
    public void SetData(string format, object data, bool autoConvert) => inner.SetData(format, data, autoConvert);
    public void SetData(string format, object data) => inner.SetData(format, data);
    public void SetData(Type format, object data) => inner.SetData(format, data);
    public void SetData(object data) => inner.SetData(data);
}
