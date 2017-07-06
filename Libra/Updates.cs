﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;
using Gemini;
using Gemini.Contracts;

namespace Libra
{

	public partial class LibraMain
	{

		private string price_str(string currency, MarketDataEvent e)
		{
			if (LastTrades[currency] != null)
			{
				var diff = " (" + (e.Price - LastTrades[currency].Price).ToString("+0.00;-0.00") + ")";
				return e.Price.ToString() + diff;
			}
			return e.Price.ToString();
		}
		/// <summary>
		/// Update the connection status label with current prices
		/// </summary>
		private void UpdateTicker(string currency, MarketDataEvent e)
		{
			var ticker = Symbols
				.Aggregate(new StringBuilder(), (sb, s) => sb.Append(String.Format("{0}: {1}  ", s.ToUpper(), s == currency ? e.Price : LastTrades[s]?.Price)), sb => sb.ToString());
			connectionStatusLabel.Text = String.Format("Connected: {0}   {1}", GeminiClient.Wallet.Key(), ticker);

			if (currency == null)
			{
				tbBtcUsdPrice.Text = LastTrades["btcusd"]?.Price.ToString();
				tbEthUsdPrice.Text = LastTrades["ethusd"]?.Price.ToString();
				tbEthBtcPrice.Text = LastTrades["ethbtc"]?.Price.ToString();
			}
			else if (currency == "btcusd")
			{
				tbBtcUsdPrice.Text = price_str(currency, e);
			}
			else if (currency == "ethbtc")
			{
				tbEthBtcPrice.Text = price_str(currency, e);
			}
			else if (currency == "ethusd")
			{
				tbEthUsdPrice.Text = price_str(currency, e);
			}

			tbBtcUsdVwap.Text = V["btcusd"] != 0 ? Math.Round(PV["btcusd"] / V["btcusd"], 2).ToString() : "Calculating";
			tbEthUsdVwap.Text = V["ethusd"] != 0 ? Math.Round(PV["ethusd"] / V["ethusd"], 2).ToString() : "Calculating";
			tbEthBtcVwap.Text = V["ethbtc"] != 0 ? Math.Round(PV["ethbtc"] / V["ethbtc"], 4).ToString() : "Calculating";
		}

		/// <summary>
		/// Update status strip with runtime and current prices
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void UpdateRuntime(object sender, EventArgs e)
		{
			string[] symbols = { "btcusd", "ethusd", "ethbtc" };
			var uptime = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
			uptimeStatusLabel.Text = TimeSpan.FromSeconds(Math.Round(uptime.TotalSeconds, 0)).ToString();

			if (LastHeartbeat != 0)
			{
				if ((DateTime.UtcNow.ToTimestampMs() - LastHeartbeat) > 6000)
				{
					MessageBox.Show(String.Format("Last heartbeat received {0} ms ago!", (DateTime.UtcNow.ToTimestampMs() - LastHeartbeat)));
					OrderEventStart(null, null);
					LastHeartbeat = 0;
				}
					
			}
		}

		/// <summary>
		/// Update account balances and total USD value
		/// </summary>
		private void UpdateAccounts(object sender, EventArgs e)
		{
			try
			{
				var assets = 0.0M;
				var balances = GeminiClient.GetBalances();
				foreach (var balance in balances)
				{
					switch (balance.Currency)
					{
						case "BTC":
							textboxBtcBalance.Text = balance.Amount.ToString();
							assets += balance.Amount * LastTrades["btcusd"].Price;
							break;
						case "ETH":
							textboxEthBalance.Text = balance.Amount.ToString();
							assets += balance.Amount * LastTrades["ethusd"].Price;
							break;
						case "USD":
							textboxUsdBalance.Text = balance.Amount.ToString();
							assets += balance.Amount;
							break;
					}
				}
				labelAssetValue.Text = String.Format("Total Value: ${0}", Math.Round(assets, 2));
			}
			catch { };
		}


		/// <summary>
		/// Callback for when an order is placed, or status has changed
		/// </summary>
		/// <param name="state"></param>
		/// <param name="e"></param>
		private void UpdateOrders(string type, object data)
		{
			
			var order = (OrderEvent)data;
			
			if (type == "closed")
            {
                TreeNode[] found;
                if ((found = treeOrders.Nodes.Find(order.OrderID, true)).Count() > 0)
                    found.First().Remove();

                OrderTracker.Orders[order.OrderID] = order;
                if (order.IsCancelled)
                {
                    treeOrders.Nodes["Cancelled"].Nodes.Add(order.OrderID, order.OrderID);
                }
                else if (order.ExecutedAmount == order.OriginalAmount)
                {
                    treeOrders.Nodes["Filled"].Nodes.Add(order.OrderID, order.OrderID);
                }
				    
			}
			else if (type == "cancelled")
			{
				order = (OrderEventCancelled)data;
				//treeOrders.Nodes["Cancelled"].Nodes.Add(order.OrderID, order.OrderID);
				
			}
			else if (type == "booked" || type == "initial")
			{
				treeOrders.Nodes["Active"].Nodes.Add(order.OrderID, order.OrderID);
				OrderTracker.Orders[order.OrderID] = order;
			}
			else if (type == "filled")
			{
				order = (OrderEventFilled)data;
				OrderTracker.Orders[order.OrderID] = order;
			}


			foreach (var n in OrderTracker.Pending)
			{
                //treeOrders.Nodes["Pending"].Nodes.Find(n.ClientOrderID, false)?.First()?.Remove();
                if (treeOrders.Nodes["Pending"].Nodes.Find(n.ClientOrderID, false).Count() > 0)
                    treeOrders.Nodes["Pending"].Nodes.Find(n.ClientOrderID, false).First().Remove();
                treeOrders.Nodes["Pending"].Nodes.Add(n.ClientOrderID, n.ClientOrderID);
			}

			UpdateAccounts(null, null);
		}

		private void UpdatePending(NewOrderRequest n)
		{
			
		}


	}
}