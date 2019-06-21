﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace SharpBladeFlightAnalyzer
{
	public class ULogFile
	{
		private static Dictionary<string, int> typeSize;

		UInt64 timestamp;
		byte version;

		BinaryReader reader;

		ulong[] appendedOffset;

		//ID,msgName
		Dictionary<ushort, string> msgNameDict;
		//msgName,fieldList<Type,fieldName>        
		Dictionary<string, List<Tuple<string, string>>> fieldNameDict;
		//fieldFullName,DataField
		Dictionary<string, DataField> fieldDict;
		//key,value
		List<Tuple<string, string>> infomations;
		//key,value
		List<Tuple<string, float>> parameters;

		List<LoggedMessage> messages;

		public ulong Timestamp
		{
			get { return timestamp; }
			set { timestamp = value; }
		}

		public byte Version
		{
			get { return version; }
			set { version = value; }
		}

		public List<Tuple<string, string>> Infomations
		{
			get { return infomations; }
			set { infomations = value; }
		}

		public static Dictionary<string, int> TypeSize
		{
			get
			{
				if(typeSize==null)
				{
					typeSize = new Dictionary<string, int>();
					for(int i=8;i<=64;i<<=1)
					{
						typeSize.Add("int" + i.ToString() + "_t", i >> 3);
						typeSize.Add("uint" + i.ToString() + "_t", i >> 3);
					}
					typeSize.Add("float", 4);
					typeSize.Add("double", 8);
					typeSize.Add("char", 1);
					typeSize.Add("bool", 1);
				}
				return typeSize;
			}
		}

		public List<Tuple<string, float>> Parameters
		{
			get { return parameters; }
			set { parameters = value; }
		}

		public ULogFile()
		{
			appendedOffset = new UInt64[3] { 0, 0, 0 };
			msgNameDict=new Dictionary<ushort, string>();
			fieldNameDict=new Dictionary<string, List<Tuple<string, string>>>();
			fieldDict=new Dictionary<string, DataField>();
			infomations=new List<Tuple<string, string>>();
			parameters=new List<Tuple<string, float>>();
			messages = new List<LoggedMessage>();
		}

		public bool Load(string path, int buffsize)
		{
			FileInfo fi = new FileInfo(path);
			reader = new BinaryReader(new FileStream(path, FileMode.Open));
			byte[] buff = reader.ReadBytes(8);
			if (buff[0] != 0x55)
				return false;
			if (buff[1] != 0x4c)
				return false;
			if (buff[2] != 0x6f)
				return false;
			if (buff[3] != 0x67)
				return false;
			if (buff[4] != 0x01)
				return false;
			if (buff[5] != 0x12)
				return false;
			if (buff[6] != 0x35)
				return false;
			version = buff[7];
			timestamp = reader.ReadUInt64();
			while (readMessage()) ;
			return true;
		}

		private string readASCIIString(int n)
		{
			byte[] buff = reader.ReadBytes(n);
			return Encoding.ASCII.GetString(buff, 0, n).TrimEnd('\0');
		}

		private bool readMessage()
		{
			if (reader.BaseStream.Length - reader.BaseStream.Position < 3)
				return false;
			ushort size = reader.ReadUInt16();
			byte msgtype = reader.ReadByte();
			if (reader.BaseStream.Length - reader.BaseStream.Position < size)
				return false;
			switch (msgtype)
			{
				case 66://B
					return readFlagBitset(size);
				case 70://F
					return readFormatDefinition(size);
				case 73://I
					return readInformation(size);
				case 77://M
					return readInformationMulti(size);
				case 80://P
					return readParameter(size);
				case 65://A
					return readSubscribe(size);
				case 82://R
					return readUnsubscribe(size);
				case 68://D
					return readLoggedData(size);
				case 76://L
					return readLoggedString(size);
				case 83://S
					return readSynchronization(size);
				case 79://O
					return readDropoutMark(size);
				default:
					Debug.WriteLine("Unknow message type:{0}.", msgtype);
					return false;
			}
		}

		private bool readFlagBitset(ushort msglen)
		{
			byte[] compat = reader.ReadBytes(8);
			byte[] incompat = reader.ReadBytes(8);
			appendedOffset[0] = reader.ReadUInt64();
			appendedOffset[1] = reader.ReadUInt64();
			appendedOffset[2] = reader.ReadUInt64();
			return true;
		}

		private bool readFormatDefinition(ushort msglen)
		{
			string defstr = readASCIIString(msglen);
			int msgNameLen = defstr.IndexOf(":");
			string msgname = defstr.Substring(0, msgNameLen);
			defstr = defstr.Substring(msgNameLen + 1);
			string[] fieldNames = defstr.Split(';');

			List<Tuple<string, string>> fieldList = new List<Tuple<string, string>>();

			for (int i = 0; i < fieldNames.Length; i++)
			{
				if (fieldNames[i].Length == 0)
					continue;
				string[] field = fieldNames[i].Split(' ');
				if (field[0].IndexOf('[') > 0)
				{
					//string realType = field[0].Substring(0, field[0].IndexOf('['));
					//string lens = field[0].Substring(field[0].IndexOf('[') + 1);
					//lens = lens.Substring(0, field[0].IndexOf(']'));
					string realType = field[0];
					int len = getArrayLength(ref realType);
					for (int j = 0; j < len; j++)
					{
						fieldList.Add(new Tuple<string, string>(realType, msgname + "." + field[1] + "[" + j.ToString() + "]"));
					}
				}
				else
				{
					fieldList.Add(new Tuple<string, string>(field[0], msgname + "." + field[1]));
				}
			}
			fieldNameDict.Add(msgname, fieldList);
			return true;
		}

		private bool readInformation(ushort msglen)
		{
			int keylen = reader.ReadByte();
			string key = readASCIIString(keylen);
			string keytype = key.Substring(0, key.IndexOf(' '));
			string keyname = key.Substring(keytype.Length + 1);
			int arrlen = getArrayLength(ref keytype);
			int len = msglen - 1 - keylen;

			string info = "";
			if(!TypeSize.ContainsKey(keytype))
			{
				for(int i=0;i<len;i++)
				{
					info += reader.ReadByte().ToString("X2");
				}
			}
			else
			{
				if(len!=typeSize[keytype]*arrlen)
				{
					Debug.WriteLine("[I]:msglen not match.");
					return false;
				}
				if(keytype=="char")
				{
					info = readASCIIString(len);
				}
				else
				{
					for(int i=0;i<arrlen;i++)
					{
						switch(keytype)
						{
							case "int8_t":
								info += reader.ReadSByte().ToString();
								break;
							case "uint8_t":
								info += reader.ReadByte().ToString();
								break;
							case "int16_t":
								info += reader.ReadInt16().ToString();
								break;
							case "uint16_t":
								info += reader.ReadUInt16().ToString();
								break;
							case "int32_t":
								info += reader.ReadInt32().ToString();
								break;
							case "uint32_t":
								info += reader.ReadUInt32().ToString();
								break;
							case "int64_t":
								info += reader.ReadInt64().ToString();
								break;
							case "uint64_t":
								info += reader.ReadUInt64().ToString();
								break;
							case "float":
								info += reader.ReadSingle().ToString();
								break;
							case "double":
								info += reader.ReadDouble().ToString();
								break;
							case "bool":
								info += reader.ReadBoolean().ToString();
								break;
						}
						if (i != arrlen - 1)
							info += ",";
					}
				}
			}
			infomations.Add(new Tuple<string, string>(keyname, info));
			return true;
		}

		private bool readInformationMulti(ushort msglen)
		{
			reader.ReadByte();
			readInformation((ushort)(msglen - 1));
			return true;
		}

		private bool readParameter(ushort msglen)
		{
			int keylen = reader.ReadByte();
			string key = readASCIIString(keylen);
			string keytype = key.Substring(0, key.IndexOf(' '));
			string keyname = key.Substring(keytype.Length + 1);
			float val=0;
			switch(keytype)
			{
				case "int32_t":
					val = reader.ReadInt32();
					break;
				case "float":
					val = reader.ReadSingle();
					break;
				default:
					Debug.WriteLine("[P]:Unknow parameter type {0}.", keytype);
					return false;
			}
			parameters.Add(new Tuple<string, float>(keyname, val));
			return true;
		}

		private bool readSubscribe(ushort msglen)
		{
			byte mid = reader.ReadByte();
			ushort id = reader.ReadUInt16();
			string name = readASCIIString(msglen - 3);
			msgNameDict.Add(id, name);
			return true;
		}

		private bool readUnsubscribe(ushort msglen)
		{
			//not use
			reader.ReadBytes(msglen);
			return true;
		}

		private bool readLoggedData(ushort msglen)
		{
			//not use
			reader.ReadBytes(msglen);
			return true;
		}

		private bool readLoggedString(ushort msglen)
		{
			LoggedMessage msg = new LoggedMessage();
			msg.Level = (LogLevel)reader.ReadByte();
			msg.Timestamp = reader.ReadUInt64();
			msg.Message = readASCIIString(msglen - 9);
			return true;
		}

		private bool readSynchronization(ushort msglen)
		{
			//not use
			reader.ReadBytes(msglen);
			return true;
		}

		private bool readDropoutMark(ushort msglen)
		{
			//unhandled
			reader.ReadBytes(msglen);
			return true;
		}

		private int getArrayLength(ref string name)
		{
			int pos = name.IndexOf("[");
			int len;
			if (pos < 0)
				return 1;
			string str = name.Substring(pos + 1);
			str = str.Substring(0,str.IndexOf(']'));
			len = int.Parse(str);
			name = name.Substring(0, pos);
			return len;
		}

	}
}
