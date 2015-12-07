﻿using Dashboard.Models;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace Dashboard.Controllers
{
    public class IssuesController : Controller
    {
        public async Task<ActionResult> Index()
        {
            /*
            var client = new GitHubClient(new ProductHeaderValue("jbug-dash-app"));

            var request = new RepositoryIssueRequest();
            request.Labels.Add("Area-Compilers");
            request.Labels.Add("Bug");
            request.State = ItemState.Open;

            var issues = await client.Issue.GetAllForRepository("dotnet", "roslyn", request);
            var model = new IssuesViewModel();
            foreach (var issue in issues)
            {
                var name = issue.User.Name ?? "unassigned";
                model.Issues.Add(new IssueData() { Id = issue.Number, User = name });
            }
            */

            var model = new IssuesViewModel();
            model.Issues.Add(new IssueData() { Id = 42, User = "jaredpar"});
            model.Issues.Add(new IssueData() { Id = 13, User = "jaredpar"});
            model.Issues.Add(new IssueData() { Id = 13, User = "gafter"});

            return View(model);
        }
    }
}