﻿
using CSRedis.Internal.IO;
namespace CSRedis.Internal.Commands
{
    class RedisObject : RedisCommand<object>
    {
        public RedisObject(string command, params object[] args)
            : base(command, args)
        { }

        public override object Parse(RedisReader reader)
        {
            return reader.Read();
        }
    }
}
