﻿using Microsoft.Graph;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TodoSynchronizer.Helpers;
using TodoSynchronizer.Models.CanvasModels;
using TodoSynchronizer.Services;

namespace TodoSynchronizer.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        private string message;

        public string Message
        {
            get { return message; }
            set
            {
                message = value;
                this.RaisePropertyChanged("Message");
            }
        }

        public Dictionary<string, TodoTask> dic = null;
        public TodoTaskList canvasTaskList = null;
        public List<TodoTask> canvasTasks = null;
        public List<Course> courses = null;
        public int CourseCount, ItemCount, UpdateCount, FailedCount;

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            Message = "登录";
            var res = await MsalHelper.GetToken(this);
            if (!res.success)
            {
                MessageBox.Show(res.result);
                return;
            }
            TodoService.Token = res.result;

            var res1 = CanvasService.TryCacheLogin();
            if (!res1.success)
            {
                MessageBox.Show(res1.result);
                return;
            }

            Thread t = new Thread(Go);
            t.Start();
        }

        private async void ButtonTest_Click(object sender, RoutedEventArgs e)
        {
            Message = "登录";
            var res = await MsalHelper.GetToken(this);
            if (!res.success)
            {
                MessageBox.Show(res.result);
                return;
            }
            TodoService.Token = res.result;

            FileStream fs = new FileStream(@"C:\Users\111\Downloads\77892701_p0.png", FileMode.Open);
            AttachmentInfo info = new AttachmentInfo();
            info.AttachmentType = AttachmentType.File;
            info.Size = fs.Length;
            info.Name = "77892701_p0.png";

            MemoryStream stream = new MemoryStream();
            byte[] buffer = new byte[1024];
            int bytesRead = 0;
            while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) != 0)
            {
                stream.Write(buffer, 0, bytesRead);
            }

            TodoService.UploadAttachment("AQMkADAwATM0MDAAMS0xM2UxLWVlAGIxLTAwAi0wMAoALgAAA_qILJaukoNEkdO_6z6BimcBAFr-8yPLKcJMlzMhuV24V6IAA3FhX8EAAAA=", "AQMkADAwATM0MDAAMS0xM2UxLWVlAGIxLTAwAi0wMAoARgAAA_qILJaukoNEkdO_6z6BimcHAFr-8yPLKcJMlzMhuV24V6IAA3FhX8EAAABa--MjyynCTJczIblduFeiAANxYbM1AAAA", info, stream);
        }

        public void Go()
        {
            CourseCount = 0;
            ItemCount = 0;
            UpdateCount = 0;
            FailedCount = 0;
            Message = "检查Todo列表";
            try
            {
                var todoTaskLists = TodoService.ListLists();
                canvasTaskList = todoTaskLists.Find(x => x.DisplayName == "Canvas");

                if (canvasTaskList == null)
                    canvasTaskList = TodoService.AddTaskList(new TodoTaskList() { DisplayName = "Canvas" });
                
                if (canvasTaskList == null)
                    throw new Exception("内部错误");
            }
            catch(Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return;
            }

            Message = "读取Todo项目";
            try
            {
                canvasTasks = TodoService.ListTodoTasks(canvasTaskList.Id);
                if (canvasTasks == null)
                    throw new Exception("Todo项目为空");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return;
            }

            Message = "处理LinkedResources";
            try
            {
                dic = new Dictionary<string, TodoTask>();
                foreach (var todoTask in canvasTasks)
                {
                    if (todoTask.LinkedResources != null)
                        if (todoTask.LinkedResources.Count>0)
                        {
                            var url = todoTask.LinkedResources.First().WebUrl;
                            Uri uri;
                            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                                dic.Add(url, todoTask);
                        }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return;
            }

            Message = "读取Canvas课程列表";
            try
            {
                courses = CanvasService.ListCourses();
                if (courses == null)
                    throw new Exception("Canvas课程列表为空");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return;
            }

            try
            {
                foreach (var course in courses)
                {
                    CourseCount++;
                    ProcessAssignments($"处理课程 {course.Name} ", course);
                    ProcessAnouncements($"处理课程 {course.Name} ", course);
                    ProcessQuizes($"处理课程 {course.Name} ", course);
                    ProcessDiscussions($"处理课程 {course.Name} ", course);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return;
            }
            Message = $"完成！已处理 {CourseCount} 门课程中的 {ItemCount} 个项目，更新 {UpdateCount} 个项目";
        }

        public void ProcessAssignments(string message_prefix, Course course)
        {
            Message = message_prefix;
            try
            {
                var assignments = CanvasService.ListAssignments(course.Id.ToString());
                if (assignments == null)
                    return;
                if (assignments.Count == 0)
                    return;

                foreach (var assignment in assignments)
                {
                    var updated = false;
                    ItemCount++;
                    Message = message_prefix + $"作业 {assignment.Name}";
                    TodoTask todoTask = null;
                    if (dic.ContainsKey(assignment.HtmlUrl))
                        todoTask = dic[assignment.HtmlUrl];
                    else
                        todoTask = null;

                    //---Self & LinkedResource---//
                    TodoTask todoTaskNew = new TodoTask();
                    var res1 = UpdateAssignment(course, assignment, todoTask, todoTaskNew);
                    if (res1)
                    {
                        if (todoTask is null)
                        {
                            todoTask = TodoService.AddTask(canvasTaskList.Id.ToString(), todoTaskNew);
                            TodoService.AddLinkedResource(canvasTaskList.Id.ToString(), todoTask.Id.ToString(), new LinkedResource() { DisplayName = assignment.HtmlUrl, WebUrl = assignment.HtmlUrl, ApplicationName = "Canvas" });
                            dic.Add(assignment.HtmlUrl, todoTask);
                        }
                        else
                        {
                            todoTask = TodoService.UpdateTask(canvasTaskList.Id.ToString(), todoTask.Id.ToString(), todoTaskNew);
                        }
                        updated = true;
                    }

                    //---DueDate---//
                    if (CanvasPreference.GetDueTime(assignment) == null && todoTask.DueDateTime != null)
                    {
                        TodoService.DeleteDueDate(canvasTaskList.Id.ToString(), todoTask.Id.ToString());
                        todoTask.DueDateTime = null;
                        updated = true;
                    }

                    //---Submissions -> CheckItems---//
                    if (assignment.HasSubmittedSubmissions)
                    {
                        var links = TodoService.ListCheckItems(canvasTaskList.Id.ToString(), todoTask.Id.ToString());
                        if (assignment.IsQuizAssignment)
                        {
                            var quizsubmissions = CanvasService.ListQuizSubmissons(course.Id.ToString(), assignment.QuizId.ToString());
                            for (int i = 0; i < quizsubmissions.Count; i++)
                            {
                                ChecklistItem checkitem0 = null;
                                if (links.Count >= i+1)
                                    checkitem0 = links[i];
                                else
                                    checkitem0 = null;

                                ChecklistItem checkitem0New = new ChecklistItem();
                                var res4 = UpdateSubmissionInfo(assignment, quizsubmissions[i], checkitem0, checkitem0New, CanvasStringTemplateHelper.GetSubmissionDesc);
                                if (res4)
                                {
                                    if (checkitem0 == null)
                                    {
                                        TodoService.AddCheckItem(canvasTaskList.Id.ToString(), todoTask.Id.ToString(), checkitem0New);
                                    }
                                    else
                                    {
                                        TodoService.UpdateCheckItem(canvasTaskList.Id.ToString(), todoTask.Id.ToString(), checkitem0.Id.ToString(), checkitem0New);
                                    }
                                    updated = true;
                                }
                            }
                        }
                        else
                        {
                            var submission = CanvasService.GetAssignmentSubmisson(course.Id.ToString(), assignment.Id.ToString());

                            ChecklistItem checkitem1 = null;
                            if (links.Count >= 1)
                                checkitem1 = links[0];
                            else
                                checkitem1 = null;

                            ChecklistItem checkitem1New = new ChecklistItem();
                            var res2 = UpdateSubmissionInfo(assignment, submission, checkitem1, checkitem1New, CanvasStringTemplateHelper.GetSubmissionDesc);
                            if (res2)
                            {
                                if (checkitem1 == null)
                                {
                                    TodoService.AddCheckItem(canvasTaskList.Id.ToString(), todoTask.Id.ToString(), checkitem1New);
                                }
                                else
                                {
                                    TodoService.UpdateCheckItem(canvasTaskList.Id.ToString(), todoTask.Id.ToString(), checkitem1.Id.ToString(), checkitem1New);
                                }
                                updated = true;
                            }

                            ChecklistItem checkitem2 = null;
                            if (links.Count >= 2)
                                checkitem2 = links[1];
                            else
                                checkitem2 = null;

                            ChecklistItem checkitem2New = new ChecklistItem();
                            var res3 = UpdateSubmissionInfo(assignment, submission, checkitem2, checkitem2New, CanvasStringTemplateHelper.GetGradeDesc);
                            if (res3)
                            {
                                if (checkitem2 == null)
                                {
                                    TodoService.AddCheckItem(canvasTaskList.Id.ToString(), todoTask.Id.ToString(), checkitem2New);
                                }
                                else
                                {
                                    TodoService.UpdateCheckItem(canvasTaskList.Id.ToString(), todoTask.Id.ToString(), checkitem2.Id.ToString(), checkitem2New);
                                }
                                updated = true;
                            }
                        }
                    }
                    //---Attachments---//
                    var file_reg = new Regex(@"<a.+?instructure_file_link.+?title=""(.+?)"".+?href=""(.+?)"".+?</a>");
                    var file_matches = file_reg.Matches(assignment.Content);
                    foreach (Match match in file_matches)
                    {
                        var filename = match.Groups[1].Value;
                        var filepath = match.Groups[2].Value;


                    }

                    if (updated)
                        UpdateCount++;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return;
            }
        }

        private bool UpdateSubmissionInfo<T>(Assignment assignment, T submission, ChecklistItem checklistitemOld, ChecklistItem checklistitemNew, Func<Assignment, T, string> func)
        {
            var modified = false;
            var desc = func(assignment, submission);
            var check = !desc.Contains("未");
            if (checklistitemOld == null || checklistitemOld.IsChecked != check)
            {
                checklistitemNew.IsChecked = check;
                modified = true;
            }
            if (checklistitemOld == null || checklistitemOld.DisplayName != desc)
            {
                checklistitemNew.DisplayName = desc;
                modified = true;
            }
            return modified;
        }

        public bool UpdateAssignment(Course course, Assignment assignment, TodoTask todoTaskOld, TodoTask todoTaskNew)
        {
            var modified = false;
            var title = CanvasStringTemplateHelper.GetTitle(course, assignment);
            if (todoTaskOld == null || todoTaskOld.Title == null || title != todoTaskOld.Title)
            {
                todoTaskNew.Title = title;
                modified = true;
            }

            var content = CanvasStringTemplateHelper.GetContent(assignment);
            if (todoTaskOld == null || todoTaskOld.Body.Content == null || content != todoTaskOld.Body.Content)
            {
                todoTaskNew.Body = new ItemBody() { ContentType = BodyType.Text};
                todoTaskNew.Body.Content = content;
                modified = true;
            }

            var duetime = CanvasPreference.GetDueTime(assignment);
            if (duetime.HasValue)
            {
                var date = duetime.Value.Date.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture);
                if (todoTaskOld == null || todoTaskOld.DueDateTime == null || date != todoTaskOld.DueDateTime.DateTime)
                {
                    todoTaskNew.DueDateTime = DateTimeTimeZone.FromDateTime(duetime.Value);
                    modified = true;
                }
            }

            var remindtime = CanvasPreference.GetRemindMeTime(assignment);
            if (remindtime.HasValue)
            {
                var date = remindtime.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture);
                if (todoTaskOld == null || todoTaskOld.IsReminderOn == false || todoTaskOld.ReminderDateTime==null || date != todoTaskOld.ReminderDateTime.DateTime)
                {
                    todoTaskNew.ReminderDateTime = DateTimeTimeZone.FromDateTime(remindtime.Value);
                    todoTaskNew.IsReminderOn = true;
                    modified = true;
                }
            }

            //var important = CanvasPreference.GetAssignmentImprotance();
            //if (todoTaskOld == null || todoTaskOld.Importance.Value != (important ? Importance.High : Importance.Normal))
            //{
            //    todoTaskNew.Importance = (important ? Importance.High : Importance.Normal);
            //    modified = true;
            //}

            return modified;
        }

        private void ProcessDiscussions(string message_prefix, Course course)
        {
            
        }

        private void ProcessQuizes(string message_prefix, Course course)
        {
            
        }

        private void ProcessAnouncements(string message_prefix, Course course)
        {
            
        }

        #region INotifyPropertyChanged members

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = this.PropertyChanged;
            if (handler != null)
            {
                var e = new PropertyChangedEventArgs(propertyName);
                handler(this, e);
            }
        }
        #endregion
    }
}
