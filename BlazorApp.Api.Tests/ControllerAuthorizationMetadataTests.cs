using System.Reflection;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace BlazorApp.Api.Tests;

public class ControllerAuthorizationMetadataTests
{
    private const string LocalPurchasePushToHq = "LocalPurchase.PushToHq";

    public static IEnumerable<object[]> MenuBackedEndpointPolicies()
    {
        yield return Policy<ReactStoreUsersController>(
            nameof(ReactStoreUsersController.Grid),
            Permissions.Users.View
        );
        yield return Policy<ReactStoreUsersController>(
            nameof(ReactStoreUsersController.GetByUserGuid),
            Permissions.Users.View
        );
        yield return Policy<ReactStoreUsersController>(
            nameof(ReactStoreUsersController.GetProfile),
            Permissions.Users.View
        );
        yield return Policy<ReactStoreUsersController>(
            nameof(ReactStoreUsersController.Create),
            Permissions.Users.Create
        );
        yield return Policy<ReactStoreUsersController>(
            nameof(ReactStoreUsersController.Update),
            Permissions.Users.Edit
        );
        yield return Policy<ReactStoreUsersController>(
            nameof(ReactStoreUsersController.UpdateStatus),
            Permissions.Users.Edit
        );
        yield return Policy<ReactStoreUsersController>(
            nameof(ReactStoreUsersController.UpdatePassword),
            Permissions.Users.ResetPassword
        );

        yield return Policy<ReactLocalSupplierInvoicesController>(
            nameof(ReactLocalSupplierInvoicesController.CheckProducts),
            Permissions.LocalPurchase.Edit
        );
        yield return Policy<ReactLocalSupplierInvoicesController>(
            nameof(ReactLocalSupplierInvoicesController.BatchExecuteActions),
            Permissions.LocalPurchase.Edit
        );
        yield return Policy<ReactLocalSupplierInvoicesController>(
            nameof(ReactLocalSupplierInvoicesController.BatchUpdateDetailAction),
            Permissions.LocalPurchase.Edit
        );
        yield return Policy<ReactLocalSupplierInvoicesController>(
            nameof(ReactLocalSupplierInvoicesController.PushInvoicesToHq),
            LocalPurchasePushToHq
        );
        yield return Policy<ReactLocalSupplierInvoicesController>(
            nameof(ReactLocalSupplierInvoicesController.UpdateHqProducts),
            Permissions.LocalPurchase.Edit
        );
        yield return Policy<ReactLocalSupplierInvoicesController>(
            nameof(ReactLocalSupplierInvoicesController.UpdateHqProducts),
            LocalPurchasePushToHq
        );

        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetDomesticProducts),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetDomesticProductsAdvanced),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetFieldInfo),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetDomesticProductByCode),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetProductsBySupplierCode),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetActiveProducts),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetProductTypeStatistics),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetProductPriceStatistics),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetGridData),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GetSetItems),
            Permissions.Products.View
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.CreateDomesticProduct),
            Permissions.Products.Create
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.UpdateDomesticProduct),
            Permissions.Products.Edit
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.ToggleProductStatus),
            Permissions.Products.Edit
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.BatchUpdateProductStatus),
            Permissions.Products.Edit
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.FixDuplicateImageUrls),
            Permissions.Products.Edit
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.UpdateSetItems),
            Permissions.Products.Edit
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.DeleteDomesticProduct),
            Permissions.Products.Delete
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.BatchDeleteDomesticProducts),
            Permissions.Products.Delete
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.BatchDelete),
            Permissions.Products.Delete
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.CheckHBProductNoExists),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.CheckBarcodeExists),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GenerateNextProductNo),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.GenerateProductBarcode),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.BatchCreateDomesticProducts),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.BatchDetectProducts),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductsController>(
            nameof(DomesticProductsController.BatchCreateAndUpdateProducts),
            Permissions.DomesticPurchase.ManageProducts
        );

        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.GetGridData),
            Permissions.Products.View
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.GetSetItems),
            Permissions.Products.View
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.UpdateProduct),
            Permissions.Products.Edit
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.UpdateSetItems),
            Permissions.Products.Edit
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.BatchDelete),
            Permissions.Products.Delete
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.BatchCreateProducts),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.BatchValidateProducts),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.BatchDetectProducts),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.BatchImportConfirm),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.BatchUpdate),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.SyncToHBSales),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.BatchCreateSetProducts),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<ReactDomesticProductsController>(
            nameof(ReactDomesticProductsController.SendToHq),
            Permissions.DomesticPurchase.ManageProducts
        );

        yield return Policy<ReactProductsController>(
            nameof(ReactProductsController.CreateWithPrices),
            Permissions.StoreProducts.Create
        );

        yield return Policy<ReactContainerProductsController>(
            nameof(ReactContainerProductsController.StartCreateNewProductsJob),
            Permissions.Container.Edit
        );
        yield return Policy<ReactContainerProductsController>(
            nameof(ReactContainerProductsController.StartCreateNewProductsJob),
            Permissions.PosProducts.Manage
        );
        yield return Policy<ReactContainerProductsController>(
            nameof(ReactContainerProductsController.GetCreateNewProductsJob),
            Permissions.Container.Edit
        );
        yield return Policy<ReactContainerProductsController>(
            nameof(ReactContainerProductsController.GetCreateNewProductsJob),
            Permissions.PosProducts.Manage
        );

        yield return Policy<DomesticProductCreationController>(
            nameof(DomesticProductCreationController.CreateBatch),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductCreationController>(
            nameof(DomesticProductCreationController.GetBatchList),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductCreationController>(
            nameof(DomesticProductCreationController.GetBatchDetail),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductCreationController>(
            nameof(DomesticProductCreationController.ExportBatch),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductCreationController>(
            nameof(DomesticProductCreationController.UpdatePrivateLabelPrice),
            Permissions.DomesticPurchase.ManageProducts
        );
        yield return Policy<DomesticProductCreationController>(
            nameof(DomesticProductCreationController.UpdateBatchItems),
            Permissions.DomesticPurchase.ManageProducts
        );

        yield return Policy<ReactCashRegisterUserController>(
            nameof(ReactCashRegisterUserController.Grid),
            Permissions.Store.ManageOperations
        );
        yield return Policy<ReactCashRegisterUserController>(
            nameof(ReactCashRegisterUserController.GetByHGuid),
            Permissions.Store.ManageOperations
        );
        yield return Policy<ReactCashRegisterUserController>(
            nameof(ReactCashRegisterUserController.Create),
            Permissions.Store.ManageOperations
        );
        yield return Policy<ReactCashRegisterUserController>(
            nameof(ReactCashRegisterUserController.Update),
            Permissions.Store.ManageOperations
        );
        yield return Policy<ReactCashRegisterUserController>(
            nameof(ReactCashRegisterUserController.Delete),
            Permissions.Store.ManageOperations
        );
        yield return Policy<ReactAdvertisementsController>(
            nameof(ReactAdvertisementsController.Grid),
            Permissions.Advertisements.View
        );
        yield return Policy<ReactAdvertisementsController>(
            nameof(ReactAdvertisementsController.Get),
            Permissions.Advertisements.View
        );
        yield return Policy<ReactAdvertisementsController>(
            nameof(ReactAdvertisementsController.Create),
            Permissions.Advertisements.Edit
        );
        yield return Policy<ReactAdvertisementsController>(
            nameof(ReactAdvertisementsController.Update),
            Permissions.Advertisements.Edit
        );
        yield return Policy<ReactAdvertisementsController>(
            nameof(ReactAdvertisementsController.Delete),
            Permissions.Advertisements.Edit
        );
        yield return Policy<ReactAdvertisementsController>(
            nameof(ReactAdvertisementsController.Enable),
            Permissions.Advertisements.Edit
        );
        yield return Policy<ReactAdvertisementsController>(
            nameof(ReactAdvertisementsController.UploadSignature),
            Permissions.Advertisements.Edit
        );
        yield return Policy<ReactCashRegisterUserController>(
            nameof(ReactCashRegisterUserController.BatchDelete),
            Permissions.Store.ManageOperations
        );

        yield return Policy<ReactDeviceRegistrationController>(
            nameof(ReactDeviceRegistrationController.Grid),
            Permissions.DeviceRegistration.View
        );
        yield return Policy<ReactDeviceRegistrationController>(
            nameof(ReactDeviceRegistrationController.GetById),
            Permissions.DeviceRegistration.Manage
        );
        yield return Policy<ReactDeviceRegistrationController>(
            nameof(ReactDeviceRegistrationController.Update),
            Permissions.DeviceRegistration.Manage
        );

        yield return Policy<SeasonalCardRemainingController>(
            nameof(SeasonalCardRemainingController.GetCatalog),
            Permissions.SeasonalCards.Remaining.SubmitManagedStore
        );
        yield return Policy<SeasonalCardRemainingController>(
            nameof(SeasonalCardRemainingController.CreateSubmission),
            Permissions.SeasonalCards.Remaining.SubmitManagedStore
        );
        yield return Policy<SeasonalCardRemainingController>(
            nameof(SeasonalCardRemainingController.GetSubmissions),
            Permissions.SeasonalCards.Remaining.ViewManagedStore
        );
        yield return Policy<SeasonalCardRemainingController>(
            nameof(SeasonalCardRemainingController.GetSubmission),
            Permissions.SeasonalCards.Remaining.ViewManagedStore
        );
    }

    [Theory]
    [MemberData(nameof(MenuBackedEndpointPolicies))]
    public void MenuBackedBusinessEndpoints_RequireExpectedPolicyWithoutRoleGate(
        Type controllerType,
        string methodName,
        string expectedPolicy
    )
    {
        AssertNoRoleGate(
            controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: false),
            controllerType.Name
        );

        var method = GetDeclaredMethod(controllerType, methodName);
        var authorizeAttributes = method
            .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
            .ToList();

        AssertNoRoleGate(authorizeAttributes, $"{controllerType.Name}.{methodName}");
        Assert.Contains(authorizeAttributes, attribute => attribute.Policy == expectedPolicy);
    }

    [Theory]
    [InlineData(typeof(ReactLocalSupplierInvoicesController))]
    [InlineData(typeof(ReactStoreUsersController))]
    [InlineData(typeof(DomesticProductsController))]
    [InlineData(typeof(ReactDomesticProductsController))]
    [InlineData(typeof(DomesticProductCreationController))]
    [InlineData(typeof(ReactCashRegisterUserController))]
    [InlineData(typeof(ReactDeviceRegistrationController))]
    public void TargetControllers_DoNotUseLegacyRoleGates(Type controllerType)
    {
        AssertNoRoleGate(
            controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: false),
            controllerType.Name
        );

        var publicMethods = controllerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);

        foreach (var method in publicMethods)
        {
            if (controllerType == typeof(ReactLocalSupplierInvoicesController)
                && method.Name == nameof(ReactLocalSupplierInvoicesController.SyncFromHq))
            {
                continue;
            }

            AssertNoRoleGate(
                method.GetCustomAttributes<AuthorizeAttribute>(inherit: false),
                $"{controllerType.Name}.{method.Name}"
            );
        }
    }

    [Fact]
    public void ReactStoreUsersController_ClassRequiresAuthenticationWithoutPolicyOrRoleGate()
    {
        var authorizeAttribute = Assert.Single(
            typeof(ReactStoreUsersController).GetCustomAttributes<AuthorizeAttribute>(
                inherit: false
            )
        );

        Assert.Null(authorizeAttribute.Policy);
        Assert.True(string.IsNullOrWhiteSpace(authorizeAttribute.Roles));
    }

    [Fact]
    public void LocalPurchasePushToHqPermission_IsDefinedAndSeeded()
    {
        var field = typeof(Permissions.LocalPurchase).GetField(
            "PushToHq",
            BindingFlags.Public | BindingFlags.Static
        );

        Assert.NotNull(field);
        Assert.Equal(LocalPurchasePushToHq, field.GetRawConstantValue());
        Assert.Contains(
            PermissionSeedData.AllPermissions,
            permission => permission.Code == LocalPurchasePushToHq
        );
    }

    private static object[] Policy<TController>(string methodName, string expectedPolicy) =>
        new object[] { typeof(TController), methodName, expectedPolicy };

    private static MethodInfo GetDeclaredMethod(Type controllerType, string methodName) =>
        controllerType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly
            )
            .SingleOrDefault(method => method.Name == methodName)
        ?? throw new InvalidOperationException(
            $"{controllerType.Name}.{methodName} was not found."
        );

    private static void AssertNoRoleGate(
        IEnumerable<AuthorizeAttribute> authorizeAttributes,
        string target
    )
    {
        var roleAttributes = authorizeAttributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Roles))
            .Select(attribute => attribute.Roles)
            .ToList();

        Assert.True(
            roleAttributes.Count == 0,
            $"{target} should not use Roles authorization, but found: {string.Join(", ", roleAttributes)}"
        );
    }
}
