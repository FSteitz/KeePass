/*
  KeePass Password Safe - The Open-Source Password Manager
  Copyright (C) 2003-2007 Dominik Reichl <dominik.reichl@t-online.de>

  This program is free software; you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation; either version 2 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program; if not, write to the Free Software
  Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using KeePass.Forms;
using KeePass.Resources;
using KeePass.Util;

using KeePassLib;
using KeePassLib.Interfaces;
using KeePassLib.Resources;
using KeePassLib.Security;
using KeePassLib.Utility;

namespace KeePass.DataExchange.Formats
{
	public sealed class Spamex20070328 : FormatImporter
	{
		public override string FormatName { get { return "Spamex.com 2007-03-28"; } }
		public override string DefaultExtension { get { return ""; } }
		public override string AppGroup { get { return KPRes.WebSites; } }

		public override bool AppendsToRootGroupOnly { get { return true; } }
		public override bool RequiresFile { get { return false; } }

		public override Image SmallIcon
		{
			get { return KeePass.Properties.Resources.B16x16_WWW; }
		}

		private const string UrlDomain = "www.spamex.com";
		private const string UrlBase = "https://www.spamex.com";

		private const string UrlLoginPage = "https://www.spamex.com/tool/";
		private const string UrlIndexPage = "https://www.spamex.com/tool/listaliases.cfm";

		private const string UrlIndexAliasLink = "<a href=\"/tool/aliasinfo.cfm?v=";

		private const string UrlAccountPage = "https://www.spamex.com/tool/aliasinfo.cfm?v=";

		private const string StrTabLinksStart = "<A HREF=\"";
		private const string StrTabLinksEnd = "\" class=sheadLink>";
		private const string StrTabLinkUrl = "/tool/listaliases.cfm";

		public override void Import(PwDatabase pwStorage, Stream sInput,
			IStatusLogger slLogger)
		{
			SingleLineEditForm dlgUser = new SingleLineEditForm();
			dlgUser.InitEx("Spamex.com", KPRes.WebSiteLogin + " - " + KPRes.UserName,
				KPRes.UserNamePrompt, KeePass.Properties.Resources.B48x48_WWW);
			if(dlgUser.ShowDialog() != DialogResult.OK) return;

			SingleLineEditForm dlgPassword = new SingleLineEditForm();
			dlgPassword.InitEx("Spamex.com", KPRes.WebSiteLogin + " - " + KPRes.Password,
				KPRes.PasswordPrompt, KeePass.Properties.Resources.B48x48_WWW);
			if(dlgPassword.ShowDialog() != DialogResult.OK) return;

			StatusLoggerForm slf = new StatusLoggerForm();
			slf.InitEx(false);
			slf.Show();
			slf.StartLogging(KPRes.ImportingStatusMsg);

			RemoteCertificateValidationCallback pPrevCertCb =
				ServicePointManager.ServerCertificateValidationCallback;

			ServicePointManager.ServerCertificateValidationCallback =
				delegate(object sender, X509Certificate certificate, X509Chain chain,
					SslPolicyErrors sslPolicyErrors)
				{
					return true;
				};

			try
			{
				slf.SetText("> Spamex.com...", LogStatusType.Info);

				string strUser = dlgUser.ResultString; ;
				string strPassword = dlgPassword.ResultString;

				string strPostData = @"toollogin=&MetaDomain=&LoginEmail=" +
					strUser + @"&LoginPassword=" + strPassword + @"&Remember=1";

				List<KeyValuePair<string, string>> vCookies;
				string strMain = NetUtil.WebPageLogin(UrlLoginPage, strPostData,
					out vCookies);

				if(strMain.IndexOf("Welcome <b>" + strUser + "</b>") < 0)
				{
					MessageService.ShowWarning(KPRes.InvalidUserPassword);
					slf.EndLogging(); slf.Close();
					return;
				}

				string strIndexPage = NetUtil.WebPageGetWithCookies(UrlIndexPage,
					vCookies, UrlDomain);

				ImportIndex(pwStorage, strIndexPage, vCookies, slf);

				int nOffset = 0;
				List<string> vSubPages = new List<string>();
				while(true)
				{
					string strLink = StrUtil.GetStringBetween(strIndexPage, nOffset,
						StrTabLinksStart, StrTabLinksEnd, out nOffset);
					++nOffset;

					if(strLink.Length == 0) break;

					if(!strLink.StartsWith(StrTabLinkUrl)) continue;
					if(vSubPages.IndexOf(strLink) >= 0) continue;

					vSubPages.Add(strLink);

					string strSubPage = NetUtil.WebPageGetWithCookies(UrlBase +
						strLink, vCookies, UrlDomain);

					ImportIndex(pwStorage, strSubPage, vCookies, slf);
				}
			}
			catch
			{
				ServicePointManager.ServerCertificateValidationCallback = pPrevCertCb;
				slf.EndLogging(); slf.Close();
				throw;
			}

			ServicePointManager.ServerCertificateValidationCallback = pPrevCertCb;
			slf.EndLogging(); slf.Close();
		}

		private static void ImportIndex(PwDatabase pwStorage, string strIndexPage,
			List<KeyValuePair<string, string>> vCookies, IStatusLogger slf)
		{
			int nOffset = 0;
			while(true)
			{
				int nStart = strIndexPage.IndexOf(UrlIndexAliasLink, nOffset);
				if(nStart < 0) break;

				nStart += UrlIndexAliasLink.Length;

				StringBuilder sbCode = new StringBuilder();
				while(true)
				{
					if(strIndexPage[nStart] == '\"') break;
					sbCode.Append(strIndexPage[nStart]);
					++nStart;
				}

				ImportAccount(pwStorage, sbCode.ToString(), vCookies, slf);

				nOffset = nStart + 1;
			}
		}

		private static void ImportAccount(PwDatabase pwStorage, string strID,
			List<KeyValuePair<string, string>> vCookies, IStatusLogger slf)
		{
			string strPage = NetUtil.WebPageGetWithCookies(UrlAccountPage +
				strID, vCookies, UrlDomain);

			PwEntry pe = new PwEntry(pwStorage.RootGroup, true, true);
			pwStorage.RootGroup.Entries.Add(pe);

			string str;

			string strTitle = StrUtil.GetStringBetween(strPage, 0, "Subject : <b>", "</b>");
			if(strTitle.StartsWith("<b>")) strTitle = strTitle.Substring(3, strTitle.Length - 3);
			pe.Strings.Set(PwDefs.TitleField, new ProtectedString(
				pwStorage.MemoryProtection.ProtectTitle, strTitle));

			string strUser = StrUtil.GetStringBetween(strPage, 0, "Site Username : <b>", "</b>");
			if(strUser.StartsWith("<b>")) strUser = strUser.Substring(3, strUser.Length - 3);
			pe.Strings.Set(PwDefs.UserNameField, new ProtectedString(
				pwStorage.MemoryProtection.ProtectUserName, strUser));

			str = StrUtil.GetStringBetween(strPage, 0, "Site Password : <b>", "</b>");
			if(str.StartsWith("<b>")) str = str.Substring(3, str.Length - 3);
			pe.Strings.Set(PwDefs.PasswordField, new ProtectedString(
				pwStorage.MemoryProtection.ProtectPassword, str));

			str = StrUtil.GetStringBetween(strPage, 0, "Site URL : <b>", "</b>");
			if(str.StartsWith("<b>")) str = str.Substring(3, str.Length - 3);
			pe.Strings.Set(PwDefs.UrlField, new ProtectedString(
				pwStorage.MemoryProtection.ProtectUrl, str));

			str = StrUtil.GetStringBetween(strPage, 0, "Notes : <b>", "</b>");
			if(str.StartsWith("<b>")) str = str.Substring(3, str.Length - 3);
			pe.Strings.Set(PwDefs.NotesField, new ProtectedString(
				pwStorage.MemoryProtection.ProtectNotes, str));

			str = StrUtil.GetStringBetween(strPage, 0, "Address:&nbsp;</td><td><font class=\"midHD\">", "</font></td>");
			if(str.StartsWith("<b>")) str = str.Substring(3, str.Length - 3);
			pe.Strings.Set("Address", new ProtectedString(false, str));

			str = StrUtil.GetStringBetween(strPage, 0, "Forwards to: <b>", "</b>");
			if(str.StartsWith("<b>")) str = str.Substring(3, str.Length - 3);
			pe.Strings.Set("Forward To", new ProtectedString(false, str));

			str = StrUtil.GetStringBetween(strPage, 0, "Reply-To Messages: <b>", "</b>");
			if(str.StartsWith("<b>")) str = str.Substring(3, str.Length - 3);
			pe.Strings.Set("Reply-To Messages", new ProtectedString(false, str));

			str = StrUtil.GetStringBetween(strPage, 0, "Allow Reply From: <b>", "</b>");
			if(str.StartsWith("<b>")) str = str.Substring(3, str.Length - 3);
			pe.Strings.Set("Allow Reply From", new ProtectedString(false, str));

			str = StrUtil.GetStringBetween(strPage, 0, "Filter Mode: <b>", "</b>");
			if(str.StartsWith("<b>")) str = str.Substring(3, str.Length - 3);
			pe.Strings.Set("Filter Mode", new ProtectedString(false, str));

			str = StrUtil.GetStringBetween(strPage, 0, "Created: <b>", "</b>");
			if(str.StartsWith("<b>")) str = str.Substring(3, str.Length - 3);
			pe.Strings.Set("Created", new ProtectedString(false, str));

			slf.SetText(strTitle + " - " + strUser + " (" + strID + ")",
				LogStatusType.Info);

			if(!slf.ContinueWork())
				throw new InvalidOperationException(string.Empty);
		}
	}
}
