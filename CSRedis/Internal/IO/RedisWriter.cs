﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CSRedis.Internal
{
    class RedisWriter
    {
        const char Bulk = (char)RedisMessage.Bulk;
        const char MultiBulk = (char)RedisMessage.MultiBulk;
        const string EOL = "\r\n";

        readonly RedisEncoding _encoding;

        public RedisWriter(RedisEncoding encoding)
        {
            _encoding = encoding;
        }

        public int Write(RedisCommand command, Stream stream)
        {
            string prepared = Prepare(command);
            byte[] data = _encoding.Encoding.GetBytes(prepared);
            stream.Write(data, 0, data.Length);
            return data.Length;
        }

        public int Write(RedisCommand command, byte[] buffer, int offset)
        {
            string prepared = Prepare(command);
            return _encoding.Encoding.GetBytes(prepared, 0, prepared.Length, buffer, offset);
        }

        string Prepare(RedisCommand command)
        {
            var parts = command.Command.Split(' ');
            int length = parts.Length + command.Arguments.Length;
            StringBuilder sb = new StringBuilder();
            sb.Append(MultiBulk).Append(length).Append(EOL);

            foreach (var part in parts)
                sb.Append(Bulk).Append(_encoding.Encoding.GetByteCount(part)).Append(EOL).Append(part).Append(EOL);

            foreach (var arg in command.Arguments)
            {
                string str = String.Format(CultureInfo.InvariantCulture, "{0}", arg);
                sb.Append(Bulk).Append(_encoding.Encoding.GetByteCount(str)).Append(EOL).Append(str).Append(EOL);
            }

            return sb.ToString();
        }
    }
}
