using Postgrest.Attributes;
using Postgrest.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoChat_Client.Models
{
    [Table("contacts")]
    public class Contact : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("user_id")]
        public Guid UserId { get; set; }

        [Column("contact_id")]
        public Guid ContactId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
