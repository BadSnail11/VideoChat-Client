﻿using Postgrest.Attributes;
using Postgrest.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoChat_Client.Models
{
    [Table("calls")]
    public class Call : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("caller_id")]
        public Guid CallerId { get; set; }

        [Column("receiver_id")]
        public Guid ReceiverId { get; set; }

        [Column("started_at")]
        public DateTime StartedAt { get; set; }

        [Column("ended_at")]
        public DateTime? EndedAt { get; set; }

        [Column("status")]
        public string Status { get; set; }

        [Column("caller_ip")]
        public string CallerIp { get; set; }

        [Column("receiver_ip")]
        public string ReceiverIp { get; set; }

        [Column("caller_port")]
        public int? CallerPort { get; set; }

        [Column("receiver_port")]
        public int? ReceiverPort { get; set; }

        [ColumnAttribute("duration")]
        public string? Duration { get; set; }
    }
}
