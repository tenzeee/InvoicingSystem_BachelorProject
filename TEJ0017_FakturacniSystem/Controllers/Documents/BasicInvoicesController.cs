﻿#nullable disable
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using TEJ0017_FakturacniSystem.Models;
using TEJ0017_FakturacniSystem.Models.Document;
using TEJ0017_FakturacniSystem.Models.Document.DocumentTypes;
using TEJ0017_FakturacniSystem.Models.PaymentMethod;
using TEJ0017_FakturacniSystem.Models.Subject;

namespace TEJ0017_FakturacniSystem.Controllers
{
    public class BasicInvoicesController : Controller
    {
        private readonly ApplicationContext _context;

        public BasicInvoicesController(ApplicationContext context)
        {
            _context = context;
        }

        public static string RenderViewToString(Controller controller, string viewName, object model = null)
        {
            controller.ViewData.Model = model;
            OurCompany ourCompany = OurCompany.getInstance();
            controller.ViewData["OurCompany"] = ourCompany;

            using (var sw = new StringWriter())
            {
                IViewEngine viewEngine = controller.HttpContext.RequestServices.GetService(typeof(ICompositeViewEngine)) as ICompositeViewEngine;
                ViewEngineResult viewResult = viewEngine.FindView(controller.ControllerContext, viewName, false);
                ViewContext viewContext = new ViewContext(controller.ControllerContext, viewResult.View, controller.ViewData, controller.TempData, sw, new HtmlHelperOptions());
                viewResult.View.RenderAsync(viewContext);

                return sw.GetStringBuilder().ToString();
            }
        }

        // GET: Documents
        public async Task<IActionResult> Index()
        {
            ViewData["OurCompany"] = OurCompany.getInstance();
            var documents = await _context.BasicInvoices.Include(c => c.Customer).OrderByDescending(d => d.IssueDate).ToListAsync();

            if (documents.Count > 0)
                ViewData["FirstInvoice"] = _context.BasicInvoices.ToList().Min(d => d.IssueDate);

            return View(documents);
        }

        // POST: Documents
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(IFormCollection filterValue)
        {
            ViewData["FirstInvoice"] = _context.BasicInvoices.ToList().Min(d => d.IssueDate);
            ViewData["OurCompany"] = OurCompany.getInstance();
            DateTime minDateTime = Convert.ToDateTime(filterValue["filterMinDatetime"].ToString());
            DateTime maxDateTime = Convert.ToDateTime(filterValue["filterMaxDatetime"].ToString());

            var documents = await _context.BasicInvoices.Include(c => c.Customer).OrderByDescending(d => d.IssueDate).Where(d => (d.IssueDate >= minDateTime) && (d.IssueDate <= maxDateTime)).ToListAsync();
            if(filterValue["filterRadioPaid"] == "paid")
            {
                documents = await _context.BasicInvoices.Include(c => c.Customer).OrderByDescending(d => d.IssueDate).Where(d => (d.IssueDate >= minDateTime) && (d.IssueDate <= maxDateTime) && d.IsPaid).ToListAsync();
            }
            else if(filterValue["filterRadioPaid"] == "unpaid")
            {
                documents = await _context.BasicInvoices.Include(c => c.Customer).OrderByDescending(d => d.IssueDate).Where(d => (d.IssueDate >= minDateTime) && (d.IssueDate <= maxDateTime) && !d.IsPaid).ToListAsync();
            }

            return View(documents);
        }

        // GET: Documents/Detail/5
        public async Task<FileResult> Detail(int? id)
        {
            if (id == null)
                return null;

            var document = await _context.BasicInvoices.Include(c => c.Customer).Include(u => u.User).Include(ca => ca.Customer.Address).
                Include(b => b.BankDetail).Include(pm => pm.PaymentMethod).Include(di => di.DocumentItems).FirstOrDefaultAsync(d => d.DocumentId == id);
            if (document == null)
            {
                return null;
            }

            HtmlToPdfConvertor htmlToPdfConvertor = new HtmlToPdfConvertor();
            string outputHtml = RenderViewToString(this, "Detail", document);
            MemoryStream output = htmlToPdfConvertor.getDocumentPdf(outputHtml);
            output.Position = 0;

            return File(output, System.Net.Mime.MediaTypeNames.Application.Pdf);
        }

        // GET: Documents/Create
        public async Task<IActionResult> Create()
        {
            OurCompany ourCompany = OurCompany.getInstance();
            var bankDetails = await _context.BankDetails.Where(bd => bd.IsVisible == true).ToListAsync();
            var paymentMethods = await _context.PaymentMethods.Where(pm => pm.IsVisible == true).ToListAsync();
            var paymentMethodsOnly = paymentMethods.Except(bankDetails);

            ViewData["Customers"] = await _context.Customers.Where(c => c.IsVisible == true).ToListAsync();
            ViewData["PaymentMethods"] = paymentMethodsOnly;
            ViewData["BankDetails"] = bankDetails;
            ViewData["OurCompany"] = ourCompany;
            NumericalSeriesGenerator numericalSeriesGenerator = new NumericalSeriesGenerator();
            ViewData["NextNum"] = numericalSeriesGenerator.generateDocumentNumber();

            return View();
        }

        // POST: Documents/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(BasicInvoice basicInvoice, IFormCollection itemsValues)
        {
            //inicializace nactenych dat pro zpetne generovani
            OurCompany ourCompany = OurCompany.getInstance();
            var bankDetails =  _context.BankDetails.Where(bd => bd.IsVisible == true).ToList();
            var paymentMethods =  _context.PaymentMethods.Where(pm => pm.IsVisible == true).ToList();
            var paymentMethodsOnly = paymentMethods.Except(bankDetails);
            ViewData["Customers"] =  _context.Customers.Where(c => c.IsVisible == true).ToList();
            ViewData["PaymentMethods"] = paymentMethodsOnly;
            ViewData["BankDetails"] = bankDetails;
            ViewData["OurCompany"] = ourCompany;
            ViewData["NextNum"] = basicInvoice.DocumentNo;

            //nastaveni platce/neplatce DPH k dokumentu pro pozdejsi otevreni
            if (ourCompany.IsVatPayer)
                basicInvoice.IsWithVat = true;
            else
                basicInvoice.IsWithVat = false;

            //zpracovani polozek dokumentu
            float sum = 0;
            List<DocumentItem> documentItems = new List<DocumentItem>();
            var itemsNames = itemsValues["ItemName"];
            var itemsPrices = itemsValues["ItemPrice"];
            var itemsAmounts = itemsValues["ItemAmount"];
            var itemsUnits = itemsValues["ItemUnit"];
            var itemsVats = itemsValues["ItemVat"];

            for (int i = 0; i < itemsNames.Count; i++)
            {
                DocumentItem documentItem = new DocumentItem();
                documentItem.Name = itemsNames[i];
                documentItem.UnitPrice = float.Parse(itemsPrices[i]);
                documentItem.Amount = float.Parse(itemsAmounts[i]);
                documentItem.Unit = itemsUnits[i];
                if (ourCompany.IsVatPayer)
                {
                    documentItem.Vat = int.Parse(itemsVats[i]);
                    sum += documentItem.UnitPrice * documentItem.Amount * ((float)documentItem.Vat / 100 + 1);
                }
                else
                {
                    sum += documentItem.UnitPrice * documentItem.Amount;
                }
                documentItems.Add(documentItem);
            }

            //prirazeni listu zpracovanych polozek ke tride document
            basicInvoice.DocumentItems = documentItems;

            //zpracovani rucne zadaneho zakaznika
            if(itemsValues["customCustomerAddressSwitch"] == "1")
            {
                Address customAddress = new Address();
                customAddress.Street = itemsValues["CustomStreet"];
                customAddress.HouseNumber = itemsValues["CustomHouseNumber"];
                customAddress.City = itemsValues["CustomCity"];
                customAddress.Zip = itemsValues["CustomZip"];

                Customer customCustomer = new Customer();
                customCustomer.Name = itemsValues["CustomSubName"];
                customCustomer.Address = customAddress;

                if (itemsValues["CustomIco"] != "")
                    customCustomer.Ico = int.Parse(itemsValues["CustomIco"]);
                else
                    customCustomer.Ico = 0;

                if (itemsValues["CustomDic"] != "")
                    customCustomer.Dic = itemsValues["CustomDic"];

                basicInvoice.Customer = customCustomer;
                ViewData["IsCustomAddress"] = "1";
            }
            else
            {
                basicInvoice.Customer = _context.Customers.FirstOrDefault(m => m.Name == itemsValues["Customer"].ToString());
            }

            //prirazeni prihlaseneho uzivatele k dokumentu
            var identity = (System.Security.Claims.ClaimsIdentity)HttpContext.User.Identity;
            string userLogin = identity.Claims.FirstOrDefault(c => c.Type == "user").Value.ToString();
            basicInvoice.User = _context.Users.FirstOrDefault(m => m.Login == userLogin);

            //prirazeni dalsich udaju
            basicInvoice.PaymentMethod = _context.PaymentMethods.FirstOrDefault(m => m.Name == itemsValues["PaymentMethod"].ToString());
            basicInvoice.BankDetail = _context.BankDetails.FirstOrDefault(m => m.Name == itemsValues["BankDetail"].ToString());
            basicInvoice.IsPaid = false;
            basicInvoice.IssueDate = DateTime.Now;

            //prirazeni vychozi hlavicky, paticky pokud nebyla vyplnena
            if (basicInvoice.headerDescription == null)
                basicInvoice.headerDescription = ourCompany.HeaderDesc;
            if(basicInvoice.footerDescription == null)
                basicInvoice.footerDescription = ourCompany.FooterDesc;

            //vypocet celkove castky (vcetne pripradne slevy)
            float calcDiscountAmount = (float)-(sum * (basicInvoice.Discount / 100));
            basicInvoice.TotalAmount = (float?)Math.Round(sum + calcDiscountAmount, 2);

            //kontrola validity a zapis dokumentu
            if (ModelState.IsValid && basicInvoice.Customer != null && basicInvoice.PaymentMethod != null && basicInvoice.User != null 
                && basicInvoice.DocumentItems != null)
            {
                //kontrola duplicity dokumentu
                if (_context.Documents.FirstOrDefault(d => d.DocumentNo == basicInvoice.DocumentNo) != null)
                {
                    ViewData["ErrorMessage"] = "Faktura s tímto číslem již existuje!";
                    ViewData["BasicInvoice"] = basicInvoice;
                    return View(basicInvoice);
                }

                _context.Add(basicInvoice);
                _context.SaveChanges();

                //nastaveni ukazatele generovani ciselne rady na aktualni hodnotu
                NumericalSeriesGenerator numericalSeriesGenerator = new NumericalSeriesGenerator();
                numericalSeriesGenerator.saveChanges();
                DataInitializer.getInstance().updateOurCompanyDataInJson();

                TempData["SuccessMessage"] = "Faktura úspěšně vystavena.";
                return RedirectToAction(nameof(Index));
            }

            ViewData["BasicInvoice"] = basicInvoice;
            ViewData["ErrorMessage"] = "Chyba validity formuláře!";
            return View(basicInvoice);
        }

        // GET: Documents/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var basicInvoice = await _context.Documents.Include(pm => pm.PaymentMethod).Include(bd => bd.BankDetail).Include(di => di.DocumentItems).Include(c => c.Customer).FirstOrDefaultAsync(d => d.DocumentId == id);
            if (basicInvoice == null)
            {
                return NotFound();
            }

            OurCompany ourCompany = OurCompany.getInstance();
            var bankDetails = await _context.BankDetails.Where(bd => bd.IsVisible == true).ToListAsync();
            var paymentMethods = await _context.PaymentMethods.Where(pm => pm.IsVisible == true).ToListAsync();
            var paymentMethodsOnly = paymentMethods.Except(bankDetails);

            ViewData["Customers"] = await _context.Customers.Where(c => c.IsVisible == true).ToListAsync();
            ViewData["PaymentMethods"] = paymentMethodsOnly;
            ViewData["BankDetails"] = bankDetails;
            ViewData["OurCompany"] = ourCompany;

            return View(basicInvoice);
        }

        // POST: Documents/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BasicInvoice basicInvoice, IFormCollection itemsValues)
        {
            if (id != basicInvoice.DocumentId)
            {
                return NotFound();
            }

            //inicializace nactenych dat pro zpetne generovani
            OurCompany ourCompany = OurCompany.getInstance();
            var bankDetails = _context.BankDetails.Where(bd => bd.IsVisible == true).ToList();
            var paymentMethods = _context.PaymentMethods.Where(pm => pm.IsVisible == true).ToList();
            var paymentMethodsOnly = paymentMethods.Except(bankDetails);
            ViewData["Customers"] = _context.Customers.Where(c => c.IsVisible == true).ToList();
            ViewData["PaymentMethods"] = paymentMethodsOnly;
            ViewData["BankDetails"] = bankDetails;
            ViewData["OurCompany"] = ourCompany;

            //zpracovani polozek dokumentu
            float sum = 0;
            List<DocumentItem> documentItems = new List<DocumentItem>();
            var itemsNames = itemsValues["ItemName"];
            var itemsPrices = itemsValues["ItemPrice"];
            var itemsAmounts = itemsValues["ItemAmount"];
            var itemsUnits = itemsValues["ItemUnit"];
            var itemsVats = itemsValues["ItemVat"];

            for (int i = 0; i < itemsNames.Count; i++)
            {
                DocumentItem documentItem = new DocumentItem();
                documentItem.Name = itemsNames[i];
                string commaChange = itemsPrices[i].Replace(".", ",");
                documentItem.UnitPrice = float.Parse(commaChange);
                documentItem.Amount = float.Parse(itemsAmounts[i]);
                documentItem.Unit = itemsUnits[i];
                if (basicInvoice.IsWithVat)
                {
                    documentItem.Vat = int.Parse(itemsVats[i]);
                    sum += documentItem.UnitPrice * documentItem.Amount * ((float)documentItem.Vat / 100 + 1);
                }
                else
                {
                    sum += documentItem.UnitPrice * documentItem.Amount;
                }
                documentItems.Add(documentItem);
            }

            //prirazeni listu zpracovanych polozek ke tride document
            basicInvoice.DocumentItems = documentItems;

            //zpracovani rucne zadaneho zakaznika
            if (itemsValues["customCustomerAddressSwitch"] == "1")
            {
                Address customAddress = new Address();
                customAddress.Street = itemsValues["CustomStreet"];
                customAddress.HouseNumber = itemsValues["CustomHouseNumber"];
                customAddress.City = itemsValues["CustomCity"];
                customAddress.Zip = itemsValues["CustomZip"];

                //Customer customCustomer = new Customer();
                Customer customCustomer = _context.Customers.FirstOrDefault(m => m.Name == itemsValues["Customer"].ToString());
                customCustomer.Name = itemsValues["CustomSubName"];
                customCustomer.Address = customAddress;

                if (itemsValues["CustomIco"] != "")
                    customCustomer.Ico = int.Parse(itemsValues["CustomIco"]);
                else
                    customCustomer.Ico = 0;

                if (itemsValues["CustomDic"] != "")
                    customCustomer.Dic = itemsValues["CustomDic"];

                basicInvoice.Customer = customCustomer;
                ViewData["IsCustomAddress"] = "1";
            }
            else
            {
                basicInvoice.Customer = _context.Customers.FirstOrDefault(m => m.Name == itemsValues["Customer"].ToString());
            }

            //prirazeni prihlaseneho uzivatele k dokumentu
            var identity = (System.Security.Claims.ClaimsIdentity)HttpContext.User.Identity;
            string userLogin = identity.Claims.FirstOrDefault(c => c.Type == "user").Value.ToString();
            basicInvoice.User = _context.Users.FirstOrDefault(m => m.Login == userLogin);

            //prirazeni dalsich udaju
            basicInvoice.PaymentMethod = _context.PaymentMethods.FirstOrDefault(m => m.Name == itemsValues["PaymentMethod"].ToString());
            basicInvoice.BankDetail = _context.BankDetails.FirstOrDefault(m => m.Name == itemsValues["BankDetail"].ToString());

            //vypocet celkove castky (vcetne pripradne slevy)
            float calcDiscountAmount = (float)-(sum * (basicInvoice.Discount / 100));
            basicInvoice.TotalAmount = (float?)Math.Round(sum + calcDiscountAmount, 2);

            //kontrola validity a zapis dokumentu
            if (ModelState.IsValid && basicInvoice.Customer != null && basicInvoice.PaymentMethod != null && basicInvoice.User != null
                && basicInvoice.DocumentItems != null)
            {
                //kontrola duplicity dokumentu (mimo aktualne upravovany)
                if (_context.Documents.FirstOrDefault(d => (d.DocumentNo == basicInvoice.DocumentNo) && (d.DocumentId != basicInvoice.DocumentId)) != null)
                {
                    ViewData["ErrorMessage"] = "Faktura s tímto číslem již existuje!";
                    ViewData["BasicInvoice"] = basicInvoice;
                    return View(basicInvoice);
                }

               //odstraneni puvodnich polozek faktury
               var oldItems = _context.DocumentItems.Where(di => di.Document.DocumentId == basicInvoice.DocumentId);
                _context.DocumentItems.RemoveRange(oldItems);

                _context.Update(basicInvoice);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Změny na faktuře " + basicInvoice.DocumentNo + " uloženy.";
                return RedirectToAction(nameof(Index));
            }

            ViewData["BasicInvoice"] = basicInvoice;
            ViewData["ErrorMessage"] = "Chyba validity formuláře!";
            return View(basicInvoice);
        }

        // POST: Documents/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var document = await _context.Documents.FindAsync(id);
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Faktura smazána.";
            return RedirectToAction(nameof(Index));
        }

        private bool DocumentExists(int id)
        {
            return _context.Documents.Any(e => e.DocumentId == id);
        }

        public ContentResult CustomerData(string customerName)
        {
            Customer customer = _context.Customers.Include("Address").FirstOrDefault(m => m.Name == customerName);
            Dictionary<string, string> customerData = new Dictionary<string, string>();
            customerData.Add("customerStreet", customer.Address.Street);
            customerData.Add("customerHouseNumber", customer.Address.HouseNumber);
            customerData.Add("customerCity", customer.Address.City);
            customerData.Add("customerZip", customer.Address.Zip);
            customerData.Add("customerIco", customer.Ico.ToString());
            customerData.Add("customerDic", customer.Dic);

            string jsonResult = JsonConvert.SerializeObject(customerData);
            return Content(jsonResult);
        }

        public ContentResult IsBankMethod(string paymentMethodName)
        {
            PaymentMethod paymentMethod = _context.PaymentMethods.FirstOrDefault(m => m.Name == paymentMethodName);
            if(paymentMethod.IsBank)
                return Content("true");
            else
                return Content("false");
        }

        public async Task<IActionResult> SetPaid(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var document = await _context.Documents.FindAsync(id);
            if (document == null)
            {
                return NotFound();
            }

            document.IsPaid = true;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> SetUnpaid(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var document = await _context.Documents.FindAsync(id);
            if (document == null)
            {
                return NotFound();
            }

            document.IsPaid = false;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        public ContentResult PriceListItems(string searchString)
        {
            var items = _context.Items.Where(i => i.Name.Contains(searchString)).ToList();
            string jsonResult = JsonConvert.SerializeObject(items);

            return Content(jsonResult);
        }

        // POST: Documents/SendEmail/5
        [HttpPost, ActionName("SendEmail")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendEmail(int id, IFormCollection values)
        {
            if (id == null)
                return null;

            var document = await _context.Documents.Include(c => c.Customer).Include(u => u.User).Include(ca => ca.Customer.Address).
                Include(b => b.BankDetail).Include(pm => pm.PaymentMethod).Include(di => di.DocumentItems).FirstOrDefaultAsync(d => d.DocumentId == id);
            if (document == null)
            {
                return null;
            }

            HtmlToPdfConvertor htmlToPdfConvertor = new HtmlToPdfConvertor();
            string outputHtml = RenderViewToString(this, "Detail", document);
            MemoryStream output = htmlToPdfConvertor.getDocumentPdf(outputHtml);
            output.Position = 0;

            string subjectText = values["emailSubject"] + " " + document.DocumentNo;
            string fileName = "faktura_" + document.DocumentNo;
            EmailSender emailSender = new EmailSender(values["emailReceiver"], subjectText, values["emailText"], output, fileName);
            bool returnState = emailSender.SendEmail();

            if(returnState)
                TempData["SuccessMessage"] = "Email úspěšně odeslán.";
            else
                TempData["ErrorMessage"] = "Došlo k chybě při odesílání emailu.";

            return RedirectToAction(nameof(Index));
        }
    }
}