﻿using MT4CliWrapper;
using MT4Proxy.NET.Core;
using MT4Proxy.NET.EventArg;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace MT4Proxy.NET
{
    internal class PumpServer: ConfigBase, IServer
    {
        static PumpServer()
        {
            AllowAccounts = new List<Tuple<int, int>>();
        }

        internal override void LoadConfig(NLog.Internal.ConfigurationManager aConfig)
        {
            PumperCount = int.Parse(aConfig.AppSettings["pump_count"]);
            AllowAccount = aConfig.AppSettings["pump_allow_mt4_account"];
            if (PumperCount < 1)
            {
                string errInfo = "接收MT4推送的线程数量不正确";
                var logger = Utils.CommonLog;
                logger.Error(errInfo);
                throw new Exception(errInfo);
            }
        }

        private static IEnumerable<Tuple<int, int>> AllowAccounts;

        private static string AllowAccount
        {
            set
            {
                var groups = value.Split(',') as IEnumerable<string>;
                groups = groups.Where(i => !string.IsNullOrWhiteSpace(i));
                groups = groups.Select(i => i.Trim());
                AllowAccounts = groups.Select(i =>
                {
                    var items = i.Split('-');
                    var a = int.Parse(items[0].Trim());
                    var b = int.Parse(items[1].Trim());
                    if (b < a)
                    {
                        a = a + b;
                        b = a - b;
                        a = a - b;
                    }
                    return new Tuple<int, int>(a, b);
                }).ToArray();
            }
        }

        private static bool IsAllowAccount(int aMT4ID)
        {
            return AllowAccounts.Any(i => i.Item1 <= aMT4ID && i.Item2 >= aMT4ID);
        }

        private static int PumperCount
        { get; set; }

        private static bool EnableRunning = false;
        private List<PumpServer> _lstRunning = new List<PumpServer>();

        public void Initialize()
        {
            var logger = Utils.CommonLog;
            logger.Info("MT4订阅服务已经启动");
            EnableRunning = true;
            ServerContainer.ForkServer<CopyServer>();
            StartPump();
        }

        public void Stop()
        {
            EnableRunning = false;
            System.Threading.Thread.Sleep(1000);
            foreach (var i in _lstRunning)
            {
                i.Timer.Stop();
                i.FreeMT4(i);
            }
            ServerContainer.FinishStop();
        }

        public PumpServer()
        {

        }

        private MT4API MT4
        {
            get;
            set;
        }

        private Timer Timer
        {
            get;
            set;
        }

        private static System.Threading.Thread _tradeThread = null;
        private static ConcurrentQueue<Tuple<TRANS_TYPE, TradeRecordResult>>
            _queTrades = new ConcurrentQueue<Tuple<TRANS_TYPE, TradeRecordResult>>();
        private static System.Threading.Semaphore _tradeSignal = new System.Threading.Semaphore(0, 20000);
        private static volatile int _lastTradeTime = 0;
        private static ConcurrentBag<int> _tradeOrders = new ConcurrentBag<int>();

        private static ConcurrentQueue<Tuple<string, double, double, DateTime>>
            _queQuote = new ConcurrentQueue<Tuple<string, double, double, DateTime>>();


        private void SaveTradeProc(object aArg)
        {
            Tuple<TRANS_TYPE, TradeRecordResult> item = null;
            while (Utils.SignalWait(ref EnableRunning, _tradeSignal))
            {
                _queTrades.TryDequeue(out item);
                var a = item.Item2.order;
                var b = item.Item1.ToString();
                if (item == null || !IsAllowAccount(item.Item2.login))
                    continue;
                if (item.Item2.comment.Contains("cancelled")) continue;
                var trade = item.Item2;
                var trade_type = item.Item1;
                if (trade.timestamp > _lastTradeTime)
                {
                    _tradeOrders = new ConcurrentBag<int>();
                    _lastTradeTime = trade.timestamp;
                }
                if (_tradeOrders.Contains(trade.order))
                    continue;
                _tradeOrders.Add(trade.order);
                var handler = OnNewTrade;
                if (handler != null)
                    handler(this, new TradeInfoEventArgs(trade_type, trade));
            }
        }

        private void StartPump()
        {
            if (_tradeThread == null)
            {
                _tradeThread = new System.Threading.Thread(SaveTradeProc);
                _tradeThread.IsBackground = true;
                _tradeThread.Start();
                for (int i = 0; i < PumperCount; i++)
                {
                    var timer = new Timer(10000);
                    timer.Interval = 10000;
                    var pump = new PumpServer();
                    pump.Timer = timer;
                    timer.Elapsed += (sender, e) =>
                    {
                        pump.RestartPump(pump);
                    };
                    _lstRunning.Add(pump);
                    pump.RestartPump(pump);
                }
            }
        }

        private void RestartPump(PumpServer aPump)
        {
            aPump.Timer.Stop();
            if (!EnableRunning)
            {
                if(aPump.MT4 != null)
                {
                    FreeMT4(aPump);
                }
                return;
            }
            int retryTimes = 5;
            var mt4 = new MT4API(true);
            while(retryTimes-- > 0)
            {
                if (!mt4.IsPumpAlive())
                {
                    var logger = Utils.CommonLog;
                    logger.Warn(
                        string.Format("MT4推送接收连接建立失败，一秒之后重试，剩余机会{0}", 
                        retryTimes + 1));
                    System.Threading.Thread.Sleep(1000);
                    mt4.ConnectPump();
                    continue;
                }
                break;
            }
            if(retryTimes == -1)
            {
                var logger = Utils.CommonLog;
                logger.Error("MT4推送接收连接建立失败，请立即采取措施保障丢失的数据！");
            }
            else
            {
                mt4.OnNewTrade += WhenNewTrade;
                mt4.OnNewQuote += WhenNewQuote;
                if (aPump.MT4 != null)
                    FreeMT4(aPump);
                aPump.MT4 = mt4;
            }
            aPump.Timer.Start();
        }

        void WhenNewQuote(object sender, QuoteInfoEventArgs e)
        {
            var handler = OnNewQuote;
            if (handler != null)
                handler(sender, e);
        }

        private void WhenNewTrade(object sender, TradeInfoEventArgs e)
        {
            _queTrades.Enqueue(new Tuple<TRANS_TYPE, TradeRecordResult>(e.TradeType, e.Trade));
            _tradeSignal.Release();
        }

        public static event EventHandler<TradeInfoEventArgs> OnNewTrade = null;
        public static event EventHandler<QuoteInfoEventArgs> OnNewQuote = null;

        private void FreeMT4(PumpServer aPump)
        {
            if (aPump.MT4 != null)
            {
                aPump.MT4.OnNewTrade -= WhenNewTrade;
                aPump.MT4.OnNewQuote -= WhenNewQuote;
                aPump.MT4.Dispose();
                aPump.MT4 = null;
            }
        }
    }
}
