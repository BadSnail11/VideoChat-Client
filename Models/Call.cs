using Postgrest.Attributes;
using Postgrest.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ColumnAttribute = Postgrest.Attributes.ColumnAttribute;
using TableAttribute = Postgrest.Attributes.TableAttribute;

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

        [NotMapped]
        public string Duration { get; set; }
    }
}
