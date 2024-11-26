﻿// <copyright file="Mainwindow.xaml.cs" company="ZPM Software Inc">
// Copyright © 2024 ZPM Software Inc. All rights reserved.
// This Source Code is subject to the terms MIT Public License
// </copyright>

using Newtonsoft.Json;
using SchwabApiCS;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using static SchwabApiCS.SchwabApi;
using static SchwabApiCS.Streamer;
using static SchwabApiCS.Streamer.LevelOneEquitiesService;

namespace SchwabApiTest
{
    /// <summary>
    /// Test for SchwabApiTest
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {

        private static SchwabApi schwabApi;
        private string tokenDataFileName = "";
        private SchwabTokens schwabTokens;
        private const string title = "SchwabApiCS - Schwab API Library Test";
        private Streamer streamer;
        private string resourcesPath;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                Title = title + ", version " + SchwabApi.Version;
                DataContext = this;

                // modify tokenDataFileName to where your tokens and accountNumber for testing are located
                resourcesPath = System.IO.Directory.GetCurrentDirectory();
                var p = resourcesPath.IndexOf(@"\SchwabApiTest\");
                if (p != -1)
                    resourcesPath = resourcesPath.Substring(0, p + 15);
                tokenDataFileName = resourcesPath + "SchwabTokens.json"; // located in the project folder.


                schwabTokens = new SchwabTokens(tokenDataFileName); // gotta get the tokens First.
                if (schwabTokens.NeedsReAuthorization)
                { // use WPF dll to start web browser to capture new tokens
                    SchwabApiCS_WPF.ApiAuthorize.Open(tokenDataFileName);
                    schwabTokens = new SchwabTokens(tokenDataFileName); // reload changes
                }

                try
                {
                    schwabApi = new SchwabApi(schwabTokens);
                }
                catch (Exception ex)
                {
                    if (ex.Message.StartsWith("error: 400 GetAccessToken: Token Authorization failed.400: Bad Request")) {  // refresh token expired?
                        // RefreshTokenExpires must be incorrect. Shouldn't get here normally.
                        SchwabApiCS_WPF.ApiAuthorize.Open(tokenDataFileName);
                        schwabTokens = new SchwabTokens(tokenDataFileName); // reload changes
                        schwabApi = new SchwabApi(schwabTokens);
                    }
                    else
                        throw;
                }

                AppStart();
            }
            catch (Exception ex)
            {
                var msg = SchwabApi.ExceptionMessage(ex);
                MessageBox.Show(msg.Message, msg.Title);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public double spyMark = 0;
        public double SpyMark
        {
            get { return spyMark; }
            set { spyMark = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SpyMark")); }
        }

        public double spyAsk = 0;
        public double SpyAsk
        {
            get { return spyAsk; }
            set { spyAsk = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SpyAsk")); }
        }

        const int FixedColumns = 5;
        IList<AccountInfo> accounts;
        List<Acct> accts;

        public class SymbolItem
        {
            public string Symbol;
            public int count;
            public decimal Quantity;
        }

        public class Acct
        {
            public string AccountNumber { get; set; }
            public string AccountName { get; set; }
            public decimal LiquidationValue { get; set; }
            public decimal CashBalance { get; set; }
            public decimal DayPL { get; set; }

            public decimal[] PositionPL { get; set; }

            public string CashBalanceDisplay
            {
                get
                {
                    if (LiquidationValue == 0)
                        return CashBalance.ToString("N2") + "     ";
                    return CashBalance.ToString("N2") + ((CashBalance / LiquidationValue) * 100).ToString("N0").PadLeft(4) + "%";
                }
            }
        }

        private void AppStart()
        {
            // application code starts here =============================

            // for the Class Converter
            foreach (var st in Streamer.AccountActivityService.AccountActivityClasses)
            {
                var name = (st.Name == "ActivityObject") ? "New Activity Class" : st.Name;
                cbConvertToClass.Items.Add(new ComboBoxItem() { Content = name });
            }

            var t = Test();
            t.Wait();

            var symbols = new List<SymbolItem>(); // symbols list for accounts positions.

            foreach (var a in accounts)
            {
                if (a.securitiesAccount.positions != null)
                {

                    foreach (var p in a.securitiesAccount.positions)
                    {
                        var s = symbols.Where(r => r.Symbol == p.instrument.symbol).SingleOrDefault();
                        if (s == null)
                            symbols.Add(new SymbolItem() { Symbol = p.instrument.symbol, count = 1, Quantity = p.longQuantity > 0 ? p.longQuantity : -p.shortQuantity });
                        else
                        {
                            s.Quantity += p.longQuantity > 0 ? p.longQuantity : -p.shortQuantity;
                            s.count++;
                        }
                    }
                }
            }
            symbols = symbols.OrderByDescending(r => r.count).ThenBy(r => r.Symbol).ToList();
            accts = new List<Acct>();

            foreach (var a in accounts)
            {
                var acct = new Acct() {
                    AccountNumber = a.securitiesAccount.accountNumber,
                    AccountName = a.securitiesAccount.accountPreferences.nickName,
                    LiquidationValue = a.securitiesAccount.currentBalances.liquidationValue,
                    CashBalance = a.securitiesAccount.currentBalances.cashBalance,
                    PositionPL = new decimal[symbols.Count]
                };
                accts.Add(acct);
                if (a.securitiesAccount.positions != null)
                {
                    foreach (var p in a.securitiesAccount.positions)
                    {
                        var idx = symbols.FindIndex(r => r.Symbol == p.instrument.symbol);
                        if (idx >= 0)
                        {
                            acct.PositionPL[idx] = p.currentDayProfitLoss;
                            acct.DayPL += p.currentDayProfitLoss;
                        }
                    }
                }
            }

            while (AccountList.Columns.Count > FixedColumns) // drop and reload symbol column
                AccountList.Columns.RemoveAt(FixedColumns);

            for (var x = 0; x < symbols.Count; x++)
            {
                DataGridTextColumn textColumn = new DataGridTextColumn();
                textColumn.Binding = new System.Windows.Data.Binding("PositionPL[" + x.ToString() + "]") { StringFormat = "#.00;-.00; " };
                var s = new Style();
                s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
                s.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(8, 3, 30, 3)));
                textColumn.CellStyle = s;

                var tb = new TextBlock { Text = symbols[x].Symbol };
                textColumn.Header = tb;

                AccountList.Columns.Add(textColumn);
            }

            AccountList.ItemsSource = accts;
        }

        public void EquitiesStreamerCallback(List<Streamer.LevelOneEquity> levelOneEquities)
        {
            EquityList.Dispatcher.Invoke(() =>
            {
                EquityList.ItemsSource = null; // to force refresh
                EquityList.ItemsSource = levelOneEquities;
            });
        }

        public void OptionsStreamerCallback(List<Streamer.LevelOneOption> levelOneOptions)
        {
            OptionsList.Dispatcher.Invoke(() =>
            {
                OptionsList.ItemsSource = null; // to force refresh
                OptionsList.ItemsSource = levelOneOptions.OrderBy(r => r.Symbol).ToList();
            });
        }

        public void FuturesStreamerCallback(List<Streamer.LevelOneFuture> levelOneOptions)
        {
            OptionsList.Dispatcher.Invoke(() =>
            {
                FuturesList.ItemsSource = null; // to force refresh
                FuturesList.ItemsSource = levelOneOptions.OrderBy(r => r.Symbol).ToList();
            });
        }

        public void AccountActivityStreamerCallback(List<Streamer.AccountActivity> list)
        {
        }

        public void NasdaqBookStreamerCallback(List<Streamer.Book> list)
        {
        }

        public void NyseBookStreamerCallback(List<Streamer.Book> list)
        {
        }

        public void OptionsBookStreamerCallback(List<Streamer.Book> list)
        {
        }

        public void ChartEquitiesStreamerCallback(List<Streamer.ChartEquity> list)
        {
            OptionsList.Dispatcher.Invoke(() =>
            {
                ChartEquitiesList.ItemsSource = null; // to force refresh
                ChartEquitiesList.ItemsSource = list.OrderBy(r => r.Symbol).ToList();
            });
        }

        public void ChartFuturesStreamerCallback(List<Streamer.ChartFuture> list)
        {
            OptionsList.Dispatcher.Invoke(() =>
            {
                ChartFuturesList.ItemsSource = null; // to force refresh
                ChartFuturesList.ItemsSource = list.OrderBy(r => r.Symbol).ToList();
            });
        }

        public void LevelOneForexesStreamerCallback(List<Streamer.LevelOneForex> list)
        {
            OptionsList.Dispatcher.Invoke(() =>
            {
                ForexList.ItemsSource = null; // to force refresh
                ForexList.ItemsSource = list.OrderBy(r => r.Symbol).ToList();
            });
        }

        public void ScreenerEquitiesStreamerCallback(List<Streamer.Screener> list)
        {
        }

        public void ScreenerOptionsStreamerCallback(List<Streamer.Screener> list)
        {
        }

        /// <summary>
        /// Get Account Number For Testing
        /// by default uses first on in accounts list.
        /// </summary>
        /// <returns>account number</returns>
        private string GetAccountNumberForTesting()
        {
            try
            {
                using (StreamReader sr = new StreamReader(resourcesPath + "AccountNumberForTesting.txt"))
                {
                    var accountNumber = sr.ReadToEnd(); // file should contain just the account number
                    if (accountNumber != "")
                        return accountNumber;
                }
            }
            catch { }

            return accounts[0].securitiesAccount.accountNumber; // use first in list
        }

        public async Task<string> Test()
        {
            try  // see SchwabApi.cs for list of methods
            {

                // general account methods =====================================
                var accountHashes = schwabApi.GetAccountNumbers();  // Note: all methods will translate accountNumbers to accountHash as needed
                accounts = schwabApi.GetAccounts(true);
                var userPref = schwabApi.GetUserPreferences();

                // by default uses first on in accounts list.
                // add file AccountNumberForTesting.txt to use a different account for testing
                var accountNumber = GetAccountNumberForTesting();

                // Option Chain  =====================================
                var ocp = new OptionChainParameters
                {
                    contractType = SchwabApi.ContractType.ALL,
                    //strike = 210,
                    fromDate = DateTime.Today,
                    toDate = DateTime.Today.AddDays(30)
                };
                var aaplOptions = schwabApi.GetOptionChain("AAPL", ocp);


                // start streamer services  =====================================
                // *** NOTE ***:  ONLY ONE streamer is allowed per client.
                // creating a second streamer will throw an exception on the first when Schwab shuts down the first channel. 
                streamer = new Streamer(schwabApi);


                // EquityStreamer class is not part of SchwabApiCS, but feel free to use it.
                // It is intended for use when many application components need streaming, sometimes for the same symbol.
                // EquityStreamer.Add() can be called multiple times for the same symbol from different places and share the same streamed data.
                //
                // Normally do not use both streamer.LevelOneEquities.Request() and EquityStreamer() class at the same time.
                // They will interfere with each other.
                //
                var es = new EquityStreamer(streamer, EquitiesStreamerCallback);
                var spyData = es.Add("SPY", SpyStreamCallback, SpyPropertyCallback);  // add only one symbol at a time

                SpyMark = spyData.MarkPrice; // this is not needed to initialize SpyMark IF this was the first SPY added.
                                             // otherwise SpyMark will not be set until SPY MarkPrice changes, which wouldn't be noticeable with
                                             // an active equity like SPY, but after hours or with a lightly traded equity this will be needed
                es.Add("IWM");
                es.Add("GLD");
                es.Add("NVDA");

                //var eqList = streamer.LevelOneEquities.Request("SPY,IWM,GLD,NVDA", Streamer.LevelOneEquity.CommonFields, EquitiesStreamerCallback);
                //streamer.LevelOneEquities.Add("AAPL");
                //streamer.LevelOneEquities.Remove("AAPL"); // - works
                // streamer.LevelOneEquities.View(Streamer.LevelOneEquities.AllFields); -- not working yet

                streamer.LevelOneFutures.Request("/ES,/CL,/GC,/SI", LevelOneFuture.CommonFields, FuturesStreamerCallback);
                streamer.LevelOneForexes.Request("USD/JPY", LevelOneForex.AllFields, LevelOneForexesStreamerCallback);

                streamer.ChartEquities.Request("AAPL,SPY", ChartEquity.AllFields, ChartEquitiesStreamerCallback);
                streamer.ChartFutures.Request("/ES", ChartFuture.AllFields, ChartFuturesStreamerCallback);

                var optionSymbols = string.Join(',', aaplOptions.calls.Select(a => a.symbol).Take(10).ToArray());

                streamer.LevelOneOptions.Request(optionSymbols, LevelOneOption.AllFields, OptionsStreamerCallback);

                streamer.NasdaqBooks.Request("AAPL", Book.AllFields, NasdaqBookStreamerCallback);
                streamer.NyseBooks.Request("A", Book.AllFields, NyseBookStreamerCallback);
                streamer.OptionsBooks.Request(optionSymbols, Book.AllFields, OptionsBookStreamerCallback);

                streamer.AccountActivities.Request(AccountActivityStreamerCallback);

                // Get Quote =====================================
                var taskAppl = schwabApi.GetQuoteAsync("AAPL");
                taskAppl.Wait();
                SchwabApiCS.SchwabApi.Quote.QuotePrice applQuote = taskAppl.Result.Data.quote;
                QuoteTitle.Content = "Quote: AAPL";
                Quote.Text = JsonConvert.SerializeObject(applQuote, Formatting.Indented); // display in MainWindow
                TaskJson.Text = JsonConvert.SerializeObject(taskAppl, Formatting.Indented); // display in MainWindow

                var quotes = schwabApi.GetQuotes("IWM,SPY,USO,InvalidSymbol,MU    240809P00121000,/ES,USD/JPY", true);

                // uncomment lines below for more testing  ===========================================
                /*
                var quotes = schwabApi.GetQuotes("IWM,SPY,USO", true, "quote");
                
                var account = schwabApi.GetAccount(accountNumber, true);
                var accountOrders = schwabApi.GetOrders(accountNumber, DateTime.Today, DateTime.Today.AddDays(1));

                var marketHours = schwabApi.GetMarketHours(DateTime.Today);
                var marketHoursTomorrow = schwabApi.GetMarketHours(DateTime.Today.AddDays(1));

                var instrument1 = schwabApi.GetInstrumentByCusipId("464287655");
                var instrument2 = schwabApi.GetInstrumentsBySymbol("IWM,SPY", SchwabApi.SearchBy.fundamental);

                // 2024-06-04 Api Support acknowledged there is odd stuff going on with results - not ready for prime time.
                var movers = schwabApi.GetMovers(SchwabApi.Indexes.SPX, SchwabApi.MoversSort.PERCENT_CHANGE_UP); 
               
                var aaplOptions = schwabApi.GetOptionChain("AAPL", SchwabApi.ContractType.ALL, 2);
                var aaplExpirations = schwabApi.GetOptionExpirationChain("AAPL");

                var p = new OptionChainParameters() { contractType = ContractType.CALL, strikeCount = 2, expMonth = OptionExpMonth.JUL };
                var aaplOptions2 = schwabApi.GetOptionChain("AAPL", p);

                */

                // asynchronous requests:  blast 3 requests simultaneously ===============================
                // all the methods are available in async versions as well
                /*
                var taskAppl2 = schwabApi.GetQuoteAsync("AAPL");
                var taskQ2 = schwabApi.GetQuoteAsync("IWM", "quote");
                var taskT1 = schwabApi.GetAccountTransactionsAsync(accountNumber, DateTime.Today.AddMonths(-3),
                                                              DateTime.Now, SchwabApi.TransactionTypes.TRADE);
                Task.WaitAll(taskAppl2, taskQ2, taskT1); // wait for all 3 to complete
                var quote2 = taskQ2.Result.Data;
                var tranactions = taskT1.Result.Data;
                if (tranactions.Count > 0) // test getting transaction by id
                {
                    var trans = schwabApi.GetAccountTransaction(tranactions[0].accountNumber, tranactions[0].activityId);
                }
                */

                // uncomment to test Orders Quote =====================================
                //TestOrders(accountNumber, applQuote); // best if this is done after hours, in case order actually fills.


                // Price History =====
                var aaplDayPrices = schwabApi.GetPriceHistory("AAPL", SchwabApi.PeriodType.year, 1, SchwabApi.FrequencyType.daily,
                                                            1, null, null, false);
                var aaplDayPrices1 = schwabApi.GetPriceHistory("AAPL", SchwabApi.PeriodType.year, 1, SchwabApi.FrequencyType.daily,
                                                            1, DateTime.Today.AddDays(-8), DateTime.Today.AddDays(1), false); // this picks up todays price
                var aapl15minPrices = schwabApi.GetPriceHistory("AAPL", SchwabApi.PeriodType.day, 1, SchwabApi.FrequencyType.minute,
                                                                15, DateTime.Today.AddDays(-2), DateTime.Today.AddDays(1), true);

                //TestExceptionHandling(); // uncomment to test
            }
            catch (Exception ex)
            {
                throw; // pass up to caller.  This catch is unnessary, but helpful.  Set a breakpoint here when debugging.
            }
            return "";
        }

        // best if this is done after hours, in case order actually fills.
        public void TestOrders(string accountNumber, SchwabApiCS.SchwabApi.Quote.QuotePrice applQuote)
        {
            // === ORDERS: uncomment ones you want to test.  Best to execute after hours, or use prices that won't fill. ============================
            var pQuote = schwabApi.GetQuote("GLD"); // change this symbol to one you have a postion in

            // place a OCO bracket order to close a GLD position
            var ocoTask = schwabApi.OrderOCOBracketAsync(accountNumber, pQuote.symbol, Order.GetAssetType(pQuote.assetMainType), Order.Duration.DAY, Order.Session.NORMAL,
                                                         -1, pQuote.quote.mark + 20, pQuote.quote.mark - 20);  // qty is negative to sell

            var limitOrder = schwabApi.OrderSingle(accountNumber, pQuote.symbol, Order.GetAssetType(pQuote.assetMainType), Order.OrderType.LIMIT, Order.Session.NORMAL,
                                                   Order.Duration.DAY, Order.Position.TO_OPEN, 1, pQuote.quote.mark - 20); // -20 shouldn't fill.

            // var marketOrder = schwabApi.OrderSingle(accountNumber, pQuote.symbol, Order.GetAssetType(pQuote.assetMainType), Order.OrderType.MARKET, Order.Session.NORMAL,
            //                                         Order.Duration.DAY, Order.Position.TO_OPEN, 1);

            var orderOCO = new SchwabApiCS.Order(Order.OrderType.LIMIT, Order.OrderStrategyTypes.TRIGGER, Order.Session.NORMAL,
                                     Order.Duration.GOOD_TILL_CANCEL, 180M);
            orderOCO.Add(new Order.OrderLeg("AAPL", Order.AssetType.EQUITY, Order.Position.TO_OPEN, 1));
            var orderTriggersOCO = schwabApi.OrderTriggersOCOBracketAsync(accountNumber, orderOCO, 250M, 150M);
            orderTriggersOCO.Wait();

            var stopLoss = schwabApi.OrderStopLoss(accountNumber, pQuote.symbol, Order.GetAssetType(pQuote.assetMainType), Order.Duration.GOOD_TILL_CANCEL,
                                                   Order.Session.NORMAL, -1, pQuote.quote.mark - 10);
            if (stopLoss != null)
            {
                var result = schwabApi.OrderExecuteDelete(accountNumber, (long)stopLoss); // delete order just created
            }


            // OrderFirstTriggersSecond ==================================
            //  build first order
            var order1 = new Order(Order.OrderType.LIMIT, Order.OrderStrategyTypes.SINGLE, Order.Session.NORMAL, Order.Duration.DAY, pQuote.quote.mark - 10);
            order1.Add(new Order.OrderLeg(pQuote.symbol, Order.GetAssetType(pQuote.assetMainType), Order.Position.TO_OPEN, 1));

            // build second order
            var order2 = new Order.ChildOrderStrategy(Order.OrderType.STOP, Order.OrderStrategyTypes.SINGLE, Order.Session.NORMAL, Order.Duration.DAY, pQuote.quote.mark - 20);
            order2.Add(new Order.OrderLeg(pQuote.symbol, Order.GetAssetType(pQuote.assetMainType), Order.Position.TO_CLOSE, -1));

            // send the orders
            var orderTrigger = schwabApi.OrderTriggersSecond(accountNumber, order1, order2);
            if (orderTrigger != null)
            {
                var order = schwabApi.GetOrder(accountNumber, (long)orderTrigger);

                if (order.status != Order.Status.REJECTED.ToString())
                {
                    var result = schwabApi.OrderExecuteDelete(accountNumber, (long)orderTrigger); // delete order just created
                }
            }

            var price2 = applQuote.mark - 50; // use price that won't fill
            var orderId = schwabApi.OrderSingle(accountNumber, "AAPL", Order.AssetType.EQUITY, Order.OrderType.LIMIT,
                                              Order.Session.NORMAL, Order.Duration.GOOD_TILL_CANCEL, 1, price2); // this shouldn't fill
                                                                                                                 // what does the json order just sent look like? - add a watch for "SchwabApi.LastOrderJson"
            if (orderId != null)
            {
                var order = schwabApi.GetOrder(accountNumber, (long)orderId);
                if (order.status != Order.Status.REJECTED.ToString())
                {
                    var task = schwabApi.OrderExecuteDeleteAsync(accountNumber, (long)orderId); // delete order just created
                    task.Wait();
                    var schwabClientCorrelId = task.Result.SchwabClientCorrelId;  // this is Schwab's service reqest tracking GIUD
                }
            }


        }

        // exception handling
        // calling a non-async method will throw an error right away if request has errors.
        // An async method will throw an error when taskErr.Result.Data is accessed if taskErr.Result.HasError is true.
        //var throwsAnError = schwabApi.GetAccountTransactions("12345678", DateTime.Today.AddMonths(-3),
        //                                              DateTime.Now, SchwabApi.TransactionTypes.TRADE);

        /// <summary>
        /// test exception handling
        /// calling a non-async method will throw an error right away if request has errors.
        /// An async method will throw an error when taskErr.Result.Data is accessed if taskErr.Result.HasError is true.
        /// var throwsAnError = schwabApi.GetAccountTransactions("12345678", DateTime.Today.AddMonths(-3),
        /// </summary>
        public void TestExceptionHandling()
        {
            var taskErr = schwabApi.GetAccountTransactionsAsync("12345678", DateTime.Today.AddMonths(-3),
                                                          DateTime.Now, SchwabApi.TransactionTypes.TRADE);
            taskErr.Wait();
            //var d2 = taskErr.Result.Data; // this would throw an error right away if taskErr.Result.HasError is true

            if (!taskErr.Result.HasError) // do this to stop a throw if not desired.
            {
                var d = taskErr.Result.Data;  // safe to access data.
            } else
            { // will get there because acocunt# is bad.
                var msg = taskErr.Result.Message;
                var url = taskErr.Result.Url;
            }
        }

        private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // for .NET Core you need to add UseShellExecute = true
            // see https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.useshellexecute#property-value
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void ClassBuilder_TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try {
                var classType = ((ComboBoxItem)cbConvertToClass.SelectedValue).Content.ToString();
                var root = "";
                bool isClass = (classType == "Parent/Child classes");

                if (e == null || ((TabItem)e.AddedItems[0]).Name == "ToClass")
                {
                    var ftext = tbFromClass.Text.Replace("\r", "").Split("\n"); // from text
                    var classCode = "";  // new class

                    for (var i = 0; i < ftext.Length; i++)
                        ftext[i] = ftext[i].Trim();

                    for (var i = 0; i < ftext.Length; i++)
                    {
                        if (ftext[i] == "" || ftext[i].StartsWith("//"))
                            continue; // skip comment
                        if (ftext[i].StartsWith("public class"))
                        {
                            var className = ftext[i].Substring(12).Trim();
                            if (className == "Root")
                            {
                                var isClass_ = (classType != "Parent/Child records") ? true : isClass;
                                root = ClassBuilder_ParseClass("ClassName", isClass_, ftext, ref i);
                                if (!classType.StartsWith("Parent/Child"))
                                { // is ActivityClass, remove these properties - they are in the inherited class
                                    root = root.Replace("    public string SchwabOrderID { get; set; }\n", "")
                                               .Replace("    public string AccountNumber { get; set; }\n", "");
                                }
                                else if (!isClass)
                                    root = root.Replace("BaseEvent BaseEvent", "Base_Event BaseEvent");
                            }
                            else
                                classCode += ClassBuilder_ParseClass(className, isClass, ftext, ref i);
                        }
                    }
                    if (root != "")
                    {
                        if (isClass)
                            classCode = root.Substring(0, root.Length - 2).Trim() + "\n" + classCode.Replace("\n", "\n    ").TrimEnd() + "\n}";
                        else if (classType != "Parent/Child records")
                        {
                            root = root.Replace("ClassName", classType.Replace(" ", "") + " : ActivityObject");

                            classCode = root.Substring(0, root.Length - 2).Trim() + "\n" + classCode.Replace("\n", "\n    ").TrimEnd() + "\n}";
                        }
                        else
                            classCode = root.Trim() + classCode; //.Trim('\n');
                    }
                    if (classCode == "")
                        classCode = "No public classes found.  Is this C# code?";

                    tbToClass.Text = classCode;
                }
            }
            catch (Exception ex)
            {
                tbToClass.Text = ex.Message;
            }
        }

        private string ClassBuilder_ParseClass(string className, bool isClass, string[] ftext, ref int i)
        {
            var classCode = "";
            if (isClass)
                classCode += "\npublic class " + className + "\n{";
            else
                classCode += "\npublic record " + className + "(";

            if (++i < ftext.Length && ftext[i] != "{")
                throw new Exception("Expecting { on line " + i.ToString());
            while (++i < ftext.Length)
            {
                if (ftext[i] == "}")
                {
                    if (isClass)
                        classCode += "\n}\n";
                    else if (classCode.EndsWith("("))
                        classCode += ");";
                    else
                        classCode = classCode.Substring(0, classCode.Length - 2) + ");";
                    break;
                }
                if (ftext[i].StartsWith("public "))
                {
                    if (isClass)
                        classCode += "\n    " + ftext[i];
                    else
                        classCode += ftext[i].Substring(7).Replace(" { get; set; }", "") + ", ";
                }
                else
                    throw new Exception("unexpected text on line " + i.ToString());
            }

            if (!isClass && classCode.Length > 130) {
                var code = classCode;
                classCode = "";
                while (code.Length > 125)
                {
                    var pos = code.LastIndexOf(',', 125);
                    if (pos == -1)
                        break;
                    classCode += code.Substring(0, pos + 1);
                    code = "\n             " + code.Substring(pos + 1);

                }
                classCode += code;
            }

            return classCode.Replace("public BaseEvent", "public Base_Event")
                            .Replace("public class BaseEvent", " public class Base_Event")
                            .Replace("public record BaseEvent", " public record Base_Event");
        }

        private void cbConvertToClass_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ClassConverter != null)
            {
                if (ClassConverter.SelectedItem != ToClass)
                {
                    ClassConverter.SelectedItem = ToClass;
                }
                else
                    ClassBuilder_TabControl_SelectionChanged(null, null);
            }
        }

        /// <summary>
        /// Implementation utilizing LevelOneEquity callback and PropertyChanged
        /// When using this class, do not access SchwabApiCS LevelOneEquities directly. They will interfere with each other. 
        /// </summary>
        public class EquityStreamer
        {
            private List<SymbolItem> symbolItems = new List<SymbolItem>(); // list of symbols being watched
            private List<LevelOneEquity>? data = null;
            private Streamer streamer;
            private EquitiesCallback equitiesCallback;  // method to call when any property changes.

            /// <summary>
            /// Implementation utilizing LevelOneEquity PropertyChanged
            /// </summary>
            /// <param name="streamer"></param>
            /// <param name="callback">method to call when any property changes</param>
            public EquityStreamer(Streamer streamer, EquitiesCallback callback)
            {
                this.streamer = streamer;
                this.equitiesCallback = callback; // called when processing response completed, which could be many symbols
            }

            /// <summary>
            /// Add a SINGLE symbol to watch to an EquityStreamer with no symbol specific callbacks
            /// </summary>
            /// <param name="symbol">symbol to watch</param>
            /// <param name="callback">method to call when LevelOneEquity was changed</param>
            /// <param name="propertyCallback">call this for for every LevelOneEquity that was changed</param>
            public LevelOneEquity Add(string symbol)
            {
                return Add(symbol, null, null);
            }

            /// <summary>
            /// Add a SINGLE symbol to watch to an EquityStreamer.
            /// Multiple Adds to the same symbol is supported, and will call multiple callback methods.
            /// When last callback is removed, the streaming for the symbol will be stopped.
            /// </summary>
            /// <param name="symbol">symbol to watch</param>
            /// <param name="callback">method to call when LevelOneEquity was changed</param>
            /// <param name="propertyCallback">call this for for every LevelOneEquity that was changed</param>
            public LevelOneEquity Add(string symbol, LevelOneEquityCallback callback)
            {
                return Add(symbol, callback, null);
            }

            /// <summary>
            /// Add a SINGLE symbol to watch to an EquityStreamer.
            /// Multiple Adds to the same symbol is supported, and will call multiple callback methods.
            /// When last callback is removed, the streaming for the symbol will be stopped.
            /// </summary>
            /// <param name="symbol">symbol to watch</param>
            /// <param name="callback">method to call when LevelOneEquity was changed</param>
            /// <param name="propertyCallback">call this for for every LevelOneEquity that was changed</param>
            public LevelOneEquity Add(string symbol, PropertyCallback? propertyCallback)
            {
                return Add(symbol, null, propertyCallback);
            }

            /// <summary>
            /// Add a SINGLE symbol to watch to an EquityStreamer.
            /// Multiple Adds to the same symbol is supported, and will call multiple callback methods.
            /// When last callback is removed, the streaming for the symbol will be stopped.
            /// </summary>
            /// <param name="symbol">symbol to watch</param>
            /// <param name="callback">method to call when LevelOneEquity was changed</param>
            /// <param name="propertyCallback">call this for for every LevelOneEquity that was changed</param>
            public LevelOneEquity Add(string symbol, LevelOneEquityCallback? callback, PropertyCallback? propertyCallback)
            {
                LevelOneEquity? d;

                if (data == null) // first symbol added, initialize streamer
                {
                    data = streamer.LevelOneEquities.Request(symbol, Streamer.LevelOneEquity.CommonFields, equitiesCallback);
                    d = data.Where(data => data.key == symbol).Single();
                    d.PropertyChanged += PropertyChangedHandler;
                    d.Callback = callback;
                }
                else
                {
                    d = data.Where(data => data.key == symbol).SingleOrDefault(); // look for existing
                    if (d == null) // if symbol not found in data list, add to streamer's list/
                    {
                        streamer.LevelOneEquities.Add(symbol);
                        d = data.Where(data => data.key == symbol).Single();
                    }
                }

                var si = symbolItems.Where(r => r.Symbol == symbol).SingleOrDefault();
                if (si == null) // new symbol
                {
                    si = new SymbolItem(symbol, d);
                    symbolItems.Add(si);
                }
                si.Callbacks.Add(callback);  // list of methods to call when equity changes
                si.PropertyCallbacks.Add(propertyCallback);
                return d;
            }

            /// <summary>
            /// Remove callback from symbol list
            /// Be SURE to use same parameters as was used to Add()
            /// </summary>
            /// <param name="symbol"></param>
            /// <param name="callback"></param>
            /// <param name="propertyCallback"></param>
            public void Remove(string symbol)
            { 
                Remove(symbol, null, null);
            }

            /// <summary>
            /// Remove callback from symbol list
            /// Be SURE to use same parameters as was used to Add()
            /// </summary>
            /// <param name="symbol"></param>
            /// <param name="callback"></param>
            /// <param name="propertyCallback"></param>
            public void Remove(string symbol, LevelOneEquityCallback callback)
            {
                Remove(symbol, callback, null);
            }

            /// <summary>
            /// Remove callback from symbol list
            /// Be SURE to use same parameters as was used to Add()
            /// </summary>
            /// <param name="symbol"></param>
            /// <param name="callback"></param>
            /// <param name="propertyCallback"></param>
            public void Remove(string symbol, PropertyCallback? propertyCallback)
            {
                Remove(symbol, null, propertyCallback);
            }

            /// <summary>
            /// Remove callback from symbol list
            /// Be SURE to use same parameters as was used to Add()
            /// </summary>
            /// <param name="symbol"></param>
            /// <param name="callback"></param>
            /// <param name="propertyCallback"></param>
            public void Remove(string symbol, LevelOneEquityCallback? callback, PropertyCallback? propertyCallback)
            {
                if (data != null)
                {
                    var si = symbolItems.Where(r => r.Symbol == symbol).SingleOrDefault();
                    if (si != null) {
                        var cb = si.Callbacks.Where(r => r == callback).SingleOrDefault();
                        if (cb != null)
                            si.Callbacks.Remove(cb);
                        
                        var pc = si.PropertyCallbacks.Where(r => r == propertyCallback).SingleOrDefault();
                        if (pc != null)
                            si.PropertyCallbacks.Remove(pc);

                        if (si.Callbacks.Count <= 0 && si.PropertyCallbacks.Count <= 0)
                        { // stop streaming this symbol, no callbacks left.
                            streamer.LevelOneEquities.Remove(symbol);
                            symbolItems.Remove(si);
                        }
                    }
                }
            }

            public delegate void PropertyCallback(LevelOneEquity data, string fieldName);


            /// <summary>
            /// Called by SchwabApiCS Streamer class when equity changes
            /// calls symbol callback for all callbacks in the list 
            /// </summary>
            /// <param name="sender">LevelOneEquity object</param>
            /// <param name="args">has PropertyName that was changed in sender</param>
            public void PropertyChangedHandler(object? sender, PropertyChangedEventArgs args)
            {
                var symbol = ((LevelOneEquity)sender).key;
                var si = symbolItems.Where(r => r.Symbol == symbol).SingleOrDefault();
                if (si != null)
                {
                    foreach (var pc in si.PropertyCallbacks)
                        pc(((LevelOneEquity)sender), (string)args.PropertyName);
                }
            }


            public class SymbolItem
            {
                public SymbolItem(string symbol, LevelOneEquity data)
                {
                    Symbol = symbol;
                    Data = data;
                    Callbacks = new List<LevelOneEquityCallback>();
                    PropertyCallbacks = new List<PropertyCallback>();
                }

                public string Symbol { get; set; }
                public List<LevelOneEquityCallback> Callbacks { get; set; }
                public List<PropertyCallback> PropertyCallbacks { get; set; }

                public LevelOneEquity Data { get; set; }
            }
        }

/// <summary>
/// Whenever a LevelOneEquities response is processed for SPY, this is called.
/// It will typically be called multiple times per SPY response, once for every property changed
/// </summary>
/// <param name="data"></param>
/// <param name="propertyName"></param>
public void SpyPropertyCallback(LevelOneEquity data, string propertyName)
{
    switch (propertyName)
    {
        case "MarkPrice": SpyMark = data.MarkPrice; break;  // this will only be called when data.MarkPrice was changed.
    }
}


        /// <summary>
        /// Whenever a LevelOneEquities response is processed for SPY, this is called.
        /// </summary>
        /// <param name="data"></param>
        public void SpyStreamCallback(LevelOneEquity data)
        {
            SpyAsk = data.MarkPrice;  // updates every time a response is processed for SPY, REGARDLESS if changed or not
        }
    }
}

