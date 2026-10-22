using Soundie.ViewModels.Popups;

namespace Soundie.Test.Presentation;

public class ProjectNamePopupVMTests
{
    [Test]
    public void EmptyDraftUsesSuggestionUntilUserTypes()
    {
        var vm = new ProjectNamePopupVM("Artist - Title", new HashSet<string>());

        Assert.Multiple(() =>
        {
            Assert.That(vm.EffectiveName, Is.EqualTo("Artist - Title"));
            Assert.That(vm.CanCreate, Is.True);
        });

        vm.ProjectName = "  Custom  ";
        Assert.That(vm.EffectiveName, Is.EqualTo("Custom"));
    }

    [Test]
    public void WhitespaceDraftUsesSuggestion()
    {
        var vm = new ProjectNamePopupVM(" Artist - Title ", new HashSet<string>())
        {
            ProjectName = "   "
        };

        Assert.That(vm.EffectiveName, Is.EqualTo("Artist - Title"));
        Assert.That(vm.CanCreate, Is.True);
    }

    [TestCase("A:B", "Project name contains invalid characters.")]
    [TestCase("CON", "Project name is reserved by Windows.")]
    [TestCase("name.", "Project name cannot end with a dot or space.")]
    [TestCase("name ", "Project name cannot end with a dot or space.")]
    public void InvalidEffectiveNameDisablesCreate(string value, string error)
    {
        var vm = new ProjectNamePopupVM("Valid suggestion", new HashSet<string>())
        {
            ProjectName = value
        };

        Assert.Multiple(() =>
        {
            Assert.That(vm.ValidationError, Is.EqualTo(error));
            Assert.That(vm.CanCreate, Is.False);
        });
    }

    [Test]
    public void InvalidSuggestionDisablesCreate()
    {
        var vm = new ProjectNamePopupVM("A:B", new HashSet<string>());

        Assert.That(vm.ValidationError, Is.EqualTo("Project name contains invalid characters."));
        Assert.That(vm.CanCreate, Is.False);
    }

    [Test]
    public void DuplicateNamesAreCaseInsensitive()
    {
        var vm = new ProjectNamePopupVM("Artist - Title", new HashSet<string> { "artist - title" });

        Assert.That(vm.ValidationError, Is.EqualTo("Project already exists."));
        Assert.That(vm.CanCreate, Is.False);
    }

    [Test]
    public void CorrectingDraftClearsValidationError()
    {
        var vm = new ProjectNamePopupVM("CON", new HashSet<string>());
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        vm.ProjectName = "Custom";

        Assert.Multiple(() =>
        {
            Assert.That(vm.EffectiveName, Is.EqualTo("Custom"));
            Assert.That(vm.ValidationError, Is.Null);
            Assert.That(vm.CanCreate, Is.True);
            Assert.That(changed, Does.Contain(nameof(vm.EffectiveName)));
            Assert.That(changed, Does.Contain(nameof(vm.ValidationError)));
            Assert.That(changed, Does.Contain(nameof(vm.CanCreate)));
        });
    }
}
