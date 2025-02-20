using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WheresMyKappaFapp.Models
{
    public class Feedback
    {
        public string id { get; set; }  // CosmosDB requires an Id field
        public string FeedbackArea { get; set; }
        public string FeedbackText { get; set; }
    }
}
