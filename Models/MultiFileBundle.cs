using System.Text.Json;
using FhirMarshal.Helpers;

namespace FhirMarshal.Models;

public class MultiFileBundle
{
    private int _currentBundleIndex;
    private int _totalResourceCount;

    public MultiFileBundle()
    {
        Bundles = new List<IBundle>();
        _currentBundleIndex = 0;
        _totalResourceCount = 0;
    }

    public List<IBundle> Bundles { get; }

    public int Count()
    {
        return Bundles.Count;
    }

    public int TotalResourceCount()
    {
        foreach (var bundle in Bundles)
            _totalResourceCount += bundle.Count;

        return _totalResourceCount;
    }

    public void Close()
    {
        foreach (var bundle in Bundles)
            bundle?.Close();
        _currentBundleIndex = -1;
    }

    public (Dictionary<string, JsonElement> Item, Exception? Error) Next()
    {
        if (_currentBundleIndex >= Bundles.Count)
            return (
                new Dictionary<string, JsonElement>(),
                new Exception("No more items in the bundle")
            );

        var (item, error) = Bundles[_currentBundleIndex].Next();

        if (error != null)
        {
            _currentBundleIndex++;
            return Next();
        }

        return (item, error);
    }

    public static MultiFileBundle CreateNewMultiFileBundle(IEnumerable<string> fileNames)
    {
        var result = new MultiFileBundle();

        foreach (var fileName in fileNames)
        {
            var file = new BundleFile(fileName);

            if (file?.File == null)
            {
                Console.WriteLine($"Cannot open {fileName}");
                continue;
            }

            var bundleType = Bundle.GetBundleType(file);

            if (bundleType == BundleType.UnknownBundleType)
            {
                Console.WriteLine($"Cannot determine type of {fileName}");
                // file.Close();
                continue;
            }

            file.Rewind();

            try
            {
                IBundle? bundle = bundleType switch
                {
                    BundleType.Ndjson => new NdJsonBundle(file),
                    // BundleType.Fhir => new FhirBundle(file),
                    BundleType.SingleResource => new SingleResourceBundle(file),
                    _ => null,
                };

                if (bundle != null)
                    result.Bundles.Add(bundle);
                else
                    Console.WriteLine($"Cannot create bundle for {fileName}");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        return result;
    }
}
