﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Gemini;
using Gemini.Contracts;

namespace Libra
{
	public partial class LibraMain
	{

		/* first response is the initial order book, which we don't really care about */
		static Dictionary<string, bool> initial = new Dictionary<string, bool>();

		public string[] Symbols = { "btcusd", "ethusd", "ethbtc" };
		public static Dictionary<string, MarketDataEvent> LastTrades = new Dictionary<string, MarketDataEvent>();
		public static Dictionary<string, decimal> PV = new Dictionary<string, decimal>();
		public static Dictionary<string, decimal> V = new Dictionary<string, decimal>();

		public delegate void PriceChangedDel(string currency, MarketDataEvent data);
		public delegate void OrderChangedDel(string type, object order);

		public PriceChangedDel PriceChanged;
		public OrderChangedDel OrderChanged;

		/// <summary>
		/// Seed the last trade dictionary
		/// </summary>
		public void InitialPrices()
		{
			Parallel.ForEach(Symbols, s =>
			{
				LastTrades[s] = new MarketDataEvent() { Price = GeminiClient.GetLastPrice(s), };

				PV[s] = 0;
				V[s] = 0;

				//System.Threading.ThreadPool.QueueUserWorkItem(
				//delegate (object state)
				new Task(() =>
				{
					var vwap = Vwap(s, DateTime.UtcNow.Subtract(new TimeSpan(24, 0, 0)).ToTimestamp(), DateTime.UtcNow.ToTimestamp());
					PV[s] = vwap[0];
					V[s] = vwap[1];
				}).Start();
			});

			UpdateTicker(null, null);
		}

		/// <summary>
		/// Calculate the VWAP for a period
		/// </summary>
		/// <param name="start">epoch timestamp for beginning period</param>
		/// <param name="end">epoch timestamp for end period</param>
		public static decimal[] Vwap(string currency, long start, long end)
		{
			long timestamp = start;
			decimal pv = 0;
			decimal v = 0;
			TradeHistory[] history = null;
			do
			{
				var result = Requests.Get(String.Format("https://api.gemini.com/v1/trades/{0}?limit_trades=500&since={1}", currency, timestamp)).Result;
				if (!result.IsSuccessStatusCode)
					throw new Exception("Bad response");

				history = result.Json<TradeHistory[]>();
				foreach (var trade in history)
				{
					pv += (trade.Price * trade.Amount);
					v += trade.Amount;
				}
				timestamp = history[0].Timestamp;
			} while (history.Length > 10 && (end - timestamp) > 10);

			// add to the cumulative variables
			return new decimal[] { pv, v};
		}

		public void MarketDataStart()
		{
			foreach (var currency in Symbols)
			{
				initial[currency] = true;
				var ws = new Gemini.Websocket("wss://api.gemini.com/v1/marketdata/" + currency.ToUpper(), MarketDataCallback, currency);
				ws.Connect();
			}
			
		}

		/// <summary>
		/// Start a websocket for observing order events
		/// </summary>
		public void OrderEventStart(object sender, EventArgs e)
		{

			/* a hack to just reuse the REST API client to sign out websocket headers */
			var re = new Requests();
			string url = "wss://api.gemini.com/v1/order/events";
			try
			{

				GeminiClient.Wallet.Authenticate(re, new Gemini.Contracts.PrivateRequest() { Request = "/v1/order/events" });
				if (GeminiClient.Wallet.Url().Contains("sandbox"))
					url = "wss://api.sandbox.gemini.com/v1/order/events";
				Gemini.Websocket ws = new Websocket(url, OrderEventCallback, null);

				ws.AddHeader("X-GEMINI-APIKEY", re.Headers["X-GEMINI-APIKEY"]);
				ws.AddHeader("X-GEMINI-PAYLOAD", re.Headers["X-GEMINI-PAYLOAD"]);
				ws.AddHeader("X-GEMINI-SIGNATURE", re.Headers["X-GEMINI-SIGNATURE"]);
				ws.Connect();
			}
			catch (Exception ex)
			{
				Logger.WriteException(Logger.Level.Error, ex);
				System.Windows.Forms.MessageBox.Show(ex.Message, "Error opening websocket");
			}
		}

		private void MarketDataCallback(string data, object state)
		{
			
			string currency = (string)state;
			
			if (initial[currency])
			{
				initial[currency] = false;
				return;
			}
			try
			{
				var market = data.Json<MarketData>();
				
				if (market.Type == "update")
				{
					foreach (MarketDataEvent e in market.Events)
					{
						if (e.Type == "trade")
						{
							if (LastTrades[currency].Price != e.Price)
								PriceChanged?.Invoke(currency, e);
							
							LastTrades[currency] = e;
							PV[currency] += e.Price * e.Amount;
							V[currency] += e.Amount;
						}
					}
				}
			}
			catch (Exception e) { Logger.WriteException(Logger.Level.Error, e);  }

		}

		static bool ack = true;
		static long LastHeartbeat = 0;

		private void OrderEventCallback(string data, object state)
		{
			/* This is likely caused by the serialization code below, and will be caught 
			 * by Gemini.Websocket in the Receive() loop. If we end up here, the Websocket
			 * connection is dead */
			if (data == "Exception")
			{
				var e = state as Exception;
				Logger.WriteException(Logger.Level.Fatal, e);
				return;
			}

			var pattern = "\"type\":\"\\w+\"";
			var match = Regex.Match(data, pattern).Value;

			switch(match)
			{
				case "\"type\":\"subscription_ack\"":
					return;
				case "\"type\":\"heartbeat\"":
					LastHeartbeat = data.Json<Heartbeat>().TimestampMs;
					return;
				default:
					break;
			}
	

			/* The default C# json parser is not good at mixed objects, and Gemini occasionally will send different
			 * OrderEvent objects at the same time. So we need to do a big of regex to parse out what is what */
			foreach (var item in data.TrimStart('[', '{').TrimEnd(']').Split(new string[] { "},{" }, StringSplitOptions.RemoveEmptyEntries))
			{
				string clean = "{" + item + "}";
				match = Regex.Match(clean, pattern).Value;
				//System.Windows.Forms.MessageBox.Show(match);
				switch(match)
				{
					case "\"type\":\"fill\"":
						OrderChanged?.Invoke("fill", clean.Json<OrderEventFilled>());
						break;
					case "\"type\":\"cancelled\"":
					case "\"type\":\"cancel_rejected\"":
						OrderChanged?.Invoke("cancelled", clean.Json<OrderEventCancelled>());
						break;
					default:
						var obj = clean.Json<OrderEvent>();
						OrderChanged?.Invoke(obj.Type, obj);
						break;
				}
			}
		}
	}
}