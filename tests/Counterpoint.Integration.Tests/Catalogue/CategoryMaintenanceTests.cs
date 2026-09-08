using System;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>The owner's category maintenance (SRS FR-2.20, FR-2.21).</summary>
public sealed class CategoryMaintenanceTests
{
    [Fact]
    public async Task FR_2_20_ATwoLevelCategoryCanBeCreatedEditedAndDeactivated()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();

        var parentId = await categories.CreateAsync(new SaveCategoryCommand("Plumbing", ParentId: null));
        var childId = await categories.CreateAsync(new SaveCategoryCommand("PVC Fittings", parentId));

        (await fixture.ScalarAsync($"SELECT parent_id FROM category WHERE id = {childId};"))
            .Should().Be(parentId.ToString());

        await categories.UpdateAsync(childId, new SaveCategoryCommand("PVC & Fittings", parentId));
        (await fixture.ScalarAsync($"SELECT name FROM category WHERE id = {childId};"))
            .Should().Be("PVC & Fittings");

        await categories.DeactivateAsync(childId);
        (await fixture.ScalarAsync($"SELECT active FROM category WHERE id = {childId};")).Should().Be("0");

        await categories.ReactivateAsync(childId);
        (await fixture.ScalarAsync($"SELECT active FROM category WHERE id = {childId};")).Should().Be("1");
    }

    [Fact]
    public async Task FR_2_20_CreatingAThirdLevelCategoryIsRejectedWithAPlainLanguageMessage()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();

        var parentId = await categories.CreateAsync(new SaveCategoryCommand("Plumbing", ParentId: null));
        var childId = await categories.CreateAsync(new SaveCategoryCommand("PVC Fittings", parentId));

        var thirdLevel = () => categories.CreateAsync(new SaveCategoryCommand("Elbows", childId));

        await thirdLevel.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*two levels deep*", "the message is plain language, not a raw SQLite error");

        (await fixture.CountAsync("SELECT COUNT(*) FROM category WHERE name = 'Elbows';")).Should().Be(0);
    }

    [Fact]
    public async Task FR_2_20_MovingACategoryWithChildrenUnderAnotherOneIsRejected()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();

        var plumbingId = await categories.CreateAsync(new SaveCategoryCommand("Plumbing", ParentId: null));
        await categories.CreateAsync(new SaveCategoryCommand("PVC Fittings", plumbingId));
        var electricalId = await categories.CreateAsync(new SaveCategoryCommand("Electrical", ParentId: null));

        // Plumbing already has a child; making it Electrical's child would be a third level.
        var move = () => categories.UpdateAsync(plumbingId, new SaveCategoryCommand("Plumbing", electricalId));

        await move.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sub-categories*");
    }

    [Fact]
    public async Task FR_2_1_DeletingACategoryThatHasProductsIsRefusedButDeactivatingSucceeds()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();

        var categoryId = await categories.CreateAsync(new SaveCategoryCommand("Fasteners", ParentId: null));
        await CatalogueTestProducts.CreateAsync(fixture, categoryId: categoryId);

        var delete = () => categories.DeleteAsync(categoryId);

        await delete.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot be deleted*");

        (await fixture.CountAsync($"SELECT COUNT(*) FROM category WHERE id = {categoryId};")).Should().Be(1);

        await categories.DeactivateAsync(categoryId);
        (await fixture.ScalarAsync($"SELECT active FROM category WHERE id = {categoryId};")).Should().Be("0");
    }

    [Fact]
    public async Task FR_2_20_AnUnusedCategoryCanBeDeletedOutright()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();

        var categoryId = await categories.CreateAsync(new SaveCategoryCommand("Garden", ParentId: null));

        await categories.DeleteAsync(categoryId);

        (await fixture.CountAsync($"SELECT COUNT(*) FROM category WHERE id = {categoryId};")).Should().Be(0);
    }

    [Fact]
    public async Task FR_2_20_ADuplicateNameAtTheSameLevelIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var categories = fixture.Resolve<ICategoryMaintenance>();

        await categories.CreateAsync(new SaveCategoryCommand("Tools", ParentId: null));

        var again = () => categories.CreateAsync(new SaveCategoryCommand("Tools", ParentId: null));

        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already a category*");
    }
}
