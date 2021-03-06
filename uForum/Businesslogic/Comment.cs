﻿using System;
using System.Configuration;
using System.Drawing;
using System.Linq;
using System.Web;
using System.Web.UI.MobileControls;
using System.Xml;
using Joel.Net;
using umbraco.cms.businesslogic.member;
using umbraco.presentation.channels;

namespace uForum.Businesslogic {

    [PetaPoco.TableName("forumComments")]
    [PetaPoco.PrimaryKey("id")]
    [PetaPoco.ExplicitColumns]
    public class Comment
    {

        [PetaPoco.Column("id")]
        public int Id { get; set; }
        [PetaPoco.Column("topicId")]
        public int TopicId { get; set; }
        [PetaPoco.Column("memberId")]
        public int MemberId { get; set; }
        [PetaPoco.Column("position")]
        public int Position { get; set; }

        [PetaPoco.Column("body")]
        public string Body { get; set; }
        [PetaPoco.Column("created")]
        public DateTime Created { get; set; }
        [PetaPoco.Column("score")]
        public int Score { get; set; }

        [PetaPoco.Column("isSpam")]
        public bool IsSpam { get; set; }

        public bool Exists { get; set; }
        
        private Events _e = new Events();

        public void Save()
        {
            Save(false);
        }

        public void Save(bool dontMarkAsSpam) {

            if (Id == 0) {

                if (Library.Utills.IsMember(MemberId) && !string.IsNullOrEmpty(Body)) {
                    CreateEventArgs e = new CreateEventArgs();
                    FireBeforeCreate(e);
                    if (e.Cancel) 
                        return;
                    
                    Data.SqlHelper.ExecuteNonQuery("INSERT INTO forumComments (topicId, memberId, body, position, isSpam) VALUES(@topicId, @memberId, @body, @position, @isSpam)",
                        Data.SqlHelper.CreateParameter("@topicId", TopicId),
                        Data.SqlHelper.CreateParameter("@memberId", MemberId),
                        Data.SqlHelper.CreateParameter("@body", Body),
                        Data.SqlHelper.CreateParameter("@position", Position),
                        Data.SqlHelper.CreateParameter("@isSpam", dontMarkAsSpam ? false : Forum.IsSpam(MemberId, Body, "comment"))
                        );

                    Created = DateTime.Now;
                    Id = Data.SqlHelper.ExecuteScalar<int>("SELECT MAX(id) FROM forumComments WHERE memberId = @memberId",
                        Data.SqlHelper.CreateParameter("@memberId", MemberId));

                    Topic t = Topic.GetTopic(TopicId);
                    if (t.Exists) {
                        t.Save();
                    }

                    Forum f = new Forum(t.ParentId);
                    if (f.Exists) {
                        f.SetLatestComment(Id);
                        f.SetLatestTopic(t.Id);
                        f.SetLatestAuthor(MemberId);
                        f.LatestPostDate = DateTime.Now;
                        f.Save();
                    }


                    FireAfterCreate(e);
                }

            } else {

                UpdateEventArgs e = new UpdateEventArgs();
                FireBeforeUpdate(e);

                if (!e.Cancel) {

                    Data.SqlHelper.ExecuteNonQuery("UPDATE forumComments SET topicId = @topicId, memberId = @memberId, body = @body, isSpam = @isSpam WHERE id = @id",
                        Data.SqlHelper.CreateParameter("@topicId", TopicId),
                        Data.SqlHelper.CreateParameter("@memberId", MemberId),
                        Data.SqlHelper.CreateParameter("@body", Body),
                        Data.SqlHelper.CreateParameter("@id", Id),
                        Data.SqlHelper.CreateParameter("@isSpam", dontMarkAsSpam ? false : Forum.IsSpam(MemberId, Body, "comment"))
                        );
                    FireAfterUpdate(e);
                }
            }
        }

        public bool Editable(int memberId)
        {
            if (!Exists || memberId == 0)
                return false;

            if (uForum.Library.Xslt.IsMemberInGroup("admin", memberId))
                return true;

            //TimeSpan duration = DateTime.Now - Created;
            if (memberId != MemberId)
                return false;

            return true;
        }


        public Comment() { }

        public Comment(int id)
        {
            var comment = GetComment(id, false);

            this.Id = comment.Id;
            this.TopicId = comment.TopicId;
            this.MemberId = comment.MemberId;
            this.Exists = comment.Exists;
            this.Body = comment.Body;
            this.IsSpam = comment.IsSpam;
            this.Created = comment.Created;
            this.Position = comment.Position;
        }
        
        public static Comment GetComment(int id, bool getSpamComment)
        {
            var query = string.Format("SELECT * FROM forumComments WHERE {0} id = {1}", getSpamComment ? "" : " (forumComments.isSpam IS NULL OR forumComments.isSpam != 1) AND ", id);
            
            var comment = new Comment();
            
            using (var dr = Data.SqlHelper.ExecuteReader(query))
            {
                if (dr.Read())
                {
                    comment.Id = dr.GetInt("id");
                    comment.TopicId = dr.GetInt("topicId");
                    comment.MemberId = dr.GetInt("memberId");
                    comment.Exists = true;
                    comment.Body = dr.GetString("body");
                    if (dr.IsNull("isSpam") == false)
                        comment.IsSpam = dr.GetBoolean("isSpam");

                    comment.Created = dr.GetDateTime("created");
                    comment.Position = dr.GetInt("position");
                }
                else
                {
                    comment.Exists = false;
                }
            }
            
            return comment;
        }

        public void Delete() {

            DeleteEventArgs e = new DeleteEventArgs();
            FireBeforeDelete(e);
            if (!e.Cancel) {
                Topic t = Topic.GetTopic(this.TopicId);
                Forum f = new Forum(t.ParentId);

                Data.SqlHelper.ExecuteNonQuery("DELETE FROM forumComments WHERE id = " + Id.ToString());
                Id = 0;

                t.Save(true);
                f.Save();


                FireAfterDelete(e);
            }
        
        }

        public void MarkAsSpam() {

            var e = new MarkAsSpamEventArgs();

            FireBeforeMarkAsSpam(e);

            if (e.Cancel) 
                return;

            var topic = Topic.GetTopic(TopicId);
            var forum = new Forum(topic.ParentId);

            var member = new Member(MemberId);
            var akismetApi = Forum.GetAkismetApi();
            var akismetComment = Forum.ConstructAkismetComment(member, "comment", Body);
            akismetApi.SubmitSpam(akismetComment);

            Data.SqlHelper.ExecuteNonQuery("UPDATE forumComments SET isSpam = 1 WHERE id = " + Id);
            Id = 0;

            topic.Save(true);
            forum.Save();
                
            FireAfterMarkAsSpam(e);
        }


        public static Comment Create(int topicId, string body, int memberId) {
            Comment c = new Comment();
            c.TopicId = topicId;
            c.Body = body;
            c.MemberId = memberId;
            c.Created = DateTime.Now;
            c.Position = (Topic.GetTopic(topicId).Replies) + 1;
            c.Exists = true;
            c.Save();

            return c;
        }

        public XmlNode ToXml(XmlDocument d) {
            XmlNode tx = d.CreateElement("comment");

          tx.AppendChild(umbraco.xmlHelper.addCDataNode(d, "body", Body));

            tx.Attributes.Append(umbraco.xmlHelper.addAttribute(d, "id", Id.ToString()));
            tx.Attributes.Append(umbraco.xmlHelper.addAttribute(d, "topicId", TopicId.ToString()));
            tx.Attributes.Append(umbraco.xmlHelper.addAttribute(d, "memberId", MemberId.ToString()));

            tx.Attributes.Append(umbraco.xmlHelper.addAttribute(d, "created", Created.ToString()));
            
            return tx;
        }



        /* EVENTS */
        public static event EventHandler<CreateEventArgs> BeforeCreate;
        protected virtual void FireBeforeCreate(CreateEventArgs e) {
            _e.FireCancelableEvent(BeforeCreate, this, e);
        }
        public static event EventHandler<CreateEventArgs> AfterCreate;
        protected virtual void FireAfterCreate(CreateEventArgs e) {
            if (AfterCreate != null)
                AfterCreate(this, e);
        }

        public static event EventHandler<DeleteEventArgs> BeforeDelete;
        protected virtual void FireBeforeDelete(DeleteEventArgs e) {
            _e.FireCancelableEvent(BeforeDelete, this, e);
        }
        public static event EventHandler<DeleteEventArgs> AfterDelete;
        protected virtual void FireAfterDelete(DeleteEventArgs e) {
            if (AfterDelete != null)
                AfterDelete(this, e);
        }

        public static event EventHandler<UpdateEventArgs> BeforeUpdate;
        protected virtual void FireBeforeUpdate(UpdateEventArgs e) {
            _e.FireCancelableEvent(BeforeUpdate, this, e);
        }
        public static event EventHandler<UpdateEventArgs> AfterUpdate;
        protected virtual void FireAfterUpdate(UpdateEventArgs e) {
            if (AfterUpdate != null)
                AfterUpdate(this, e);
        }
        
        public static event EventHandler<MarkAsSpamEventArgs> BeforeMarkAsSpam;
        protected virtual void FireBeforeMarkAsSpam(MarkAsSpamEventArgs e)
        {
            _e.FireCancelableEvent(BeforeMarkAsSpam, this, e);
        }
        public static event EventHandler<MarkAsSpamEventArgs> AfterMarkAsSpam;
        protected virtual void FireAfterMarkAsSpam(MarkAsSpamEventArgs e)
        {
            if (AfterMarkAsSpam != null)
                AfterMarkAsSpam(this, e);
        }
    }
}
