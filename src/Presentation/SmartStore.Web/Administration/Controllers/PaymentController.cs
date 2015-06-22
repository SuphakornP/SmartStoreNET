﻿using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using SmartStore.Admin.Models.Payments;
using SmartStore.Core.Domain.Common;
using SmartStore.Core.Domain.Payments;
using SmartStore.Core.Plugins;
using SmartStore.Services;
using SmartStore.Services.Customers;
using SmartStore.Services.Directory;
using SmartStore.Services.Localization;
using SmartStore.Services.Payments;
using SmartStore.Services.Security;
using SmartStore.Services.Shipping;
using SmartStore.Web.Framework;
using SmartStore.Web.Framework.Controllers;
using SmartStore.Web.Framework.Plugins;

namespace SmartStore.Admin.Controllers
{
	[AdminAuthorize]
    public partial class PaymentController : AdminControllerBase
	{
		#region Fields

		private readonly ICommonServices _commonServices;
        private readonly IPaymentService _paymentService;
        private readonly PaymentSettings _paymentSettings;
        private readonly IPluginFinder _pluginFinder;
		private readonly PluginMediator _pluginMediator;
		private readonly ILanguageService _languageService;
		private readonly ICustomerService _customerService;
		private readonly IShippingService _shippingService;
		private readonly ICountryService _countryService;

		#endregion

		#region Constructors

        public PaymentController(
			ICommonServices commonServices,
			IPaymentService paymentService, 
			PaymentSettings paymentSettings,
            IPluginFinder pluginFinder, 
			PluginMediator pluginMediator,
			ILanguageService languageService,
			ICustomerService customerService,
			IShippingService shippingService,
			ICountryService countryService)
		{
			this._commonServices = commonServices;
            this._paymentService = paymentService;
            this._paymentSettings = paymentSettings;
            this._pluginFinder = pluginFinder;
			this._pluginMediator = pluginMediator;
			this._languageService = languageService;
			this._customerService = customerService;
			this._shippingService = shippingService;
			this._countryService = countryService;
		}

		#endregion

		#region Utilities

		private void PreparePaymentMethodEditModel(PaymentMethodEditModel model, PaymentMethod paymentMethod)
		{
			var customerRoles = _customerService.GetAllCustomerRoles(true);
			var shippingMethods = _shippingService.GetAllShippingMethods();
			var countries = _countryService.GetAllCountries(true);

			model.AvailableCustomerRoles = new List<SelectListItem>();
			model.AvailableShippingMethods = new List<SelectListItem>();
			model.AvailableCountries = new List<SelectListItem>();

			model.AvailableCountryExclusionContextTypes = CountryRestrictionContextType.BillingAddress.ToSelectList(false).ToList();
			model.AvailableAmountRestrictionContextTypes = AmountRestrictionContextType.SubtotalAmount.ToSelectList(false).ToList();

			foreach (var role in customerRoles.OrderBy(x => x.Name))
			{
				model.AvailableCustomerRoles.Add(new SelectListItem { Text = role.Name, Value = role.Id.ToString() });
			}

			foreach (var shippingMethod in shippingMethods.OrderBy(x => x.Name))
			{
				model.AvailableShippingMethods.Add(new SelectListItem { Text = shippingMethod.GetLocalized(x => x.Name), Value = shippingMethod.Id.ToString() });
			}

			foreach (var country in countries.OrderBy(x => x.Name))
			{
				model.AvailableCountries.Add(new SelectListItem { Text = country.GetLocalized(x => x.Name), Value = country.Id.ToString() });
			}

			if (paymentMethod != null)
			{
				model.ExcludedCustomerRoleIds = paymentMethod.ExcludedCustomerRoleIds.SplitSafe(",");
				model.ExcludedShippingMethodIds = paymentMethod.ExcludedShippingMethodIds.SplitSafe(",");
				model.ExcludedCountryIds = paymentMethod.ExcludedCountryIds.SplitSafe(",");

				model.MinimumOrderAmount = paymentMethod.MinimumOrderAmount;
				model.MaximumOrderAmount = paymentMethod.MaximumOrderAmount;

				model.CountryExclusionContext = paymentMethod.CountryExclusionContext;
				model.AmountRestrictionContext = paymentMethod.AmountRestrictionContext;
			}
		}

		#endregion

		#region Methods

		public ActionResult Providers()
        {
			if (!_commonServices.Permissions.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var paymentMethodsModel = new List<PaymentMethodModel>();
            var paymentMethods = _paymentService.LoadAllPaymentMethods();
            foreach (var paymentMethod in paymentMethods)
            {
				var model = _pluginMediator.ToProviderModel<IPaymentMethod, PaymentMethodModel>(paymentMethod);
				var instance = paymentMethod.Value;
                model.IsActive = paymentMethod.IsPaymentMethodActive(_paymentSettings);
				model.SupportCapture = instance.SupportCapture;
				model.SupportPartiallyRefund = instance.SupportPartiallyRefund;
				model.SupportRefund = instance.SupportRefund;
				model.SupportVoid = instance.SupportVoid;
				model.RecurringPaymentType = instance.RecurringPaymentType.GetLocalizedEnum(_commonServices.Localization);
                paymentMethodsModel.Add(model);
            }

			return View(paymentMethodsModel);
        }

		public ActionResult ActivateProvider(string systemName, bool activate)
		{
			if (!_commonServices.Permissions.Authorize(StandardPermissionProvider.ManagePaymentMethods))
				return AccessDeniedView();

			var pm = _paymentService.LoadPaymentMethodBySystemName(systemName);

			if (activate && !pm.Value.IsActive)
			{
				NotifyWarning(_commonServices.Localization.GetResource("Admin.Configuration.Payment.CannotActivatePaymentMethod"));
			}
			else
			{
				if (!activate)
					_paymentSettings.ActivePaymentMethodSystemNames.Remove(pm.Metadata.SystemName);
				else
					_paymentSettings.ActivePaymentMethodSystemNames.Add(pm.Metadata.SystemName);

				_commonServices.Settings.SaveSetting(_paymentSettings);
				_pluginMediator.ActivateDependentWidgets(pm.Metadata, activate);
			}

			return RedirectToAction("Providers");
		}

		public ActionResult Edit(string systemName)
		{
			if (!_commonServices.Permissions.Authorize(StandardPermissionProvider.ManagePaymentMethods))
				return AccessDeniedView();

			var provider = _paymentService.LoadPaymentMethodBySystemName(systemName);
			var paymentMethod = _paymentService.GetPaymentMethodBySystemName(systemName);

			var model = _pluginMediator.ToProviderModel<IPaymentMethod, PaymentMethodEditModel>(provider, true);

			AddLocales(_languageService, model.Locales, (locale, languageId) =>
			{
				locale.FriendlyName = _pluginMediator.GetLocalizedFriendlyName(provider.Metadata, languageId, false);
				locale.Description = _pluginMediator.GetLocalizedDescription(provider.Metadata, languageId, false);
			});

			PreparePaymentMethodEditModel(model, paymentMethod);

			return View(model);
		}

		[HttpPost, ParameterBasedOnFormNameAttribute("save-continue", "continueEditing")]
		public ActionResult Edit(string systemName, bool continueEditing, PaymentMethodEditModel model)
		{
			if (!_commonServices.Permissions.Authorize(StandardPermissionProvider.ManagePaymentMethods))
				return AccessDeniedView();

			var provider = _paymentService.LoadPaymentMethodBySystemName(systemName);
			if (provider == null)
				return HttpNotFound();

			_pluginMediator.SetSetting(provider.Metadata, "FriendlyName", model.FriendlyName);
			_pluginMediator.SetSetting(provider.Metadata, "Description", model.Description);

			foreach (var localized in model.Locales)
			{
				_pluginMediator.SaveLocalizedValue(provider.Metadata, localized.LanguageId, "FriendlyName", localized.FriendlyName);
				_pluginMediator.SaveLocalizedValue(provider.Metadata, localized.LanguageId, "Description", localized.Description);
			}

			var paymentMethod = _paymentService.GetPaymentMethodBySystemName(systemName);

			if (paymentMethod == null)
				paymentMethod = new PaymentMethod { PaymentMethodSystemName = systemName };

			paymentMethod.ExcludedCustomerRoleIds = Request.Form["ExcludedCustomerRoleIds"];
			paymentMethod.ExcludedShippingMethodIds = Request.Form["ExcludedShippingMethodIds"];
			paymentMethod.ExcludedCountryIds = Request.Form["ExcludedCountryIds"];

			paymentMethod.MinimumOrderAmount = model.MinimumOrderAmount;
			paymentMethod.MaximumOrderAmount = model.MaximumOrderAmount;

			paymentMethod.CountryExclusionContext = model.CountryExclusionContext;
			paymentMethod.AmountRestrictionContext = model.AmountRestrictionContext;

			if (paymentMethod.Id == 0)
				_paymentService.InsertPaymentMethod(paymentMethod);
			else
				_paymentService.UpdatePaymentMethod(paymentMethod);

			NotifySuccess(_commonServices.Localization.GetResource("Admin.Common.DataEditSuccess"));

			return (continueEditing ?
				RedirectToAction("Edit", "Payment", new { systemName = systemName }) :
				RedirectToAction("Providers", "Payment"));
		}

        #endregion
    }
}
