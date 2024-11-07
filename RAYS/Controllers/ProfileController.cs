using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RAYS.Services;
using RAYS.ViewModels;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using RAYS.Models;

namespace RAYS.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly PostService _postService;
        private readonly UserService _userService;
        private readonly FriendService _friendService;

        public ProfileController(PostService postService, UserService userService, FriendService friendService)
        {
            _postService = postService;
            _userService = userService;
            _friendService = friendService;
        }

        public async Task<IActionResult> Profile(int userId, string viewType = "Posts")
        {
            var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
            var user = await _userService.GetUserById(userId);
            if (user == null)
            {
                return NotFound();
            }

            ViewBag.ViewType = viewType;

            // Get all relevant friend data
            var allFriendRequests = await _friendService.GetFriendRequestsAsync(currentUserId);
            var friends = await GetFriends(userId);  // Get the friends of the user being viewed

            // Check if the logged-in user and the viewed user are already friends
            var isFriend = friends.Any(f => f.UserId == currentUserId);

            // Check if there is any pending request between these two users (either direction)
            var hasPendingRequest = allFriendRequests.Any(r =>
                (r.SenderId == currentUserId && r.ReceiverId == userId && r.Status == "Pending") ||
                (r.SenderId == userId && r.ReceiverId == currentUserId && r.Status == "Pending")
            );

            var incomingRequests = allFriendRequests
                .Where(r => r.ReceiverId == currentUserId && r.Status == "Pending");

            // The view model will include:
            var userViewModel = new UserViewModel
            {
                UserId = user.Id,
                Username = user.Username,
                Posts = await GetPostsForUser(userId),
                LikedPosts = await GetLikedPostsForUser(userId),
                Friends = friends,  // Now using the friends of the user being viewed
                FriendRequests = incomingRequests,
                IsFriend = isFriend,
                HasPendingRequest = hasPendingRequest
            };

            // Now check if the user can send a friend request or not
            // If the user is already friends, don't show the friend request button
            if (isFriend)
            {
                userViewModel.HasPendingRequest = false;  // Ensure no pending request button appears
            }
            else if (hasPendingRequest)
            {
                userViewModel.HasPendingRequest = true;  // Pending request logic
            }

            return View(userViewModel);
        }




        [HttpPost]
        public async Task<IActionResult> SendFriendRequest(int receiverId)
        {
            var senderId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
            var friendRequest = new Friend { SenderId = senderId, ReceiverId = receiverId, Status = "Pending" };

            await _friendService.SendFriendRequestAsync(friendRequest);
            return RedirectToAction("Profile", new { userId = receiverId });
        }

        [HttpPost]
        public async Task<IActionResult> AcceptFriendRequest(int requestId)
        {
            // Retrieve the current user's ID
            var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            // Get all friend requests for the current user
            var friendRequests = await _friendService.GetFriendRequestsAsync(currentUserId);

            // Find the friend request by its ID
            var friendRequest = friendRequests.FirstOrDefault(r => r.Id == requestId);
            if (friendRequest == null)
            {
                return NotFound(); // If the request does not exist, handle accordingly
            }

            // Accept the friend request
            await _friendService.AcceptFriendRequestAsync(requestId);

            // Redirect to the sender's profile (the one who sent the request)
            return RedirectToAction("Profile", new { userId = friendRequest.SenderId });
        }

        [HttpPost]
        public async Task<IActionResult> RejectFriendRequest(int requestId)
        {
            // Retrieve the current user's ID
            var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            // Get all friend requests for the current user
            var friendRequests = await _friendService.GetFriendRequestsAsync(currentUserId);

            // Find the friend request by its ID
            var friendRequest = friendRequests.FirstOrDefault(r => r.Id == requestId);
            if (friendRequest == null)
            {
                return NotFound(); // If the request does not exist, handle accordingly
            }

            // Reject the friend request
            await _friendService.RejectFriendRequestAsync(requestId);

            // Redirect to the sender's profile (the one who sent the request)
            return RedirectToAction("Profile", new { userId = friendRequest.SenderId });
        }

        // Controller action to delete a friend
        [HttpPost]
        public async Task<IActionResult> DeleteFriend(int friendId)
        {
            var currentUserId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            // Call the DeleteFriendAsync method from the FriendService to remove the friendship
            var result = await _friendService.DeleteFriendAsync(currentUserId, friendId);

            if (result)
            {
                TempData["Message"] = "Friend deleted successfully!";
            }
            else
            {
                TempData["Message"] = "Failed to delete friend.";
            }

            // After deleting, redirect to the profile page of the user being viewed
            return RedirectToAction("Profile", new { userId = friendId });
        }


        private async Task<List<PostViewModel>> GetPostsForUser(int userId)
        {
            var posts = await _postService.GetByUserIdAsync(userId);
            var postViewModels = new List<PostViewModel>();

            foreach (var post in posts)
            {
                postViewModels.Add(new PostViewModel
                {
                    Id = post.Id,
                    Content = post.Content,
                    ImagePath = post.ImagePath,
                    VideoUrl = post.VideoUrl,
                    Location = post.Location,
                    CreatedAt = post.CreatedAt,
                    UserId = post.UserId,
                    IsLikedByUser = await _postService.IsPostLikedByUserAsync(userId, post.Id)
                });
            }

            return postViewModels;
        }

        private async Task<List<PostViewModel>> GetLikedPostsForUser(int userId)
        {
            var likedPosts = await _postService.GetLikedPostsByUserIdAsync(userId);
            var likedPostViewModels = new List<PostViewModel>();

            foreach (var post in likedPosts)
            {
                likedPostViewModels.Add(new PostViewModel
                {
                    Id = post.Id,
                    Content = post.Content,
                    ImagePath = post.ImagePath,
                    VideoUrl = post.VideoUrl,
                    Location = post.Location,
                    CreatedAt = post.CreatedAt,
                    UserId = post.UserId,
                    IsLikedByUser = true // All liked posts are considered liked
                });
            }

            return likedPostViewModels;
        }

        private async Task<List<UserViewModel>> GetFriends(int userId)
        {
            // Fetch the list of friends for the current user
            var friends = await _friendService.GetFriendsAsync(userId);

            var friendViewModels = new List<UserViewModel>();

            // Map each friend (either as Sender or Receiver) to a UserViewModel
            foreach (var friend in friends)
            {
                // Get the friend's user ID (exclude the current user)
                var friendId = friend.ReceiverId == userId ? friend.SenderId : friend.ReceiverId;

                // Ensure we don't include the current user in the list of friends
                if (friendId != userId)
                {
                    // Retrieve the actual user details for this friend (e.g., Username)
                    var user = await _userService.GetUserById(friendId);  // Assuming GetUserById fetches the user by their Id

                    if (user != null)
                    {
                        friendViewModels.Add(new UserViewModel
                        {
                            UserId = user.Id,
                            Username = user.Username
                        });
                    }
                }
            }

            return friendViewModels;
        }

        [HttpPost]
        public async Task<IActionResult> Like(int postId, string viewType)
        {
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            if (!await _postService.IsPostLikedByUserAsync(userId, postId))
            {
                await _postService.LikePostAsync(userId, postId);
            }

            return RedirectToAction("Profile", new { userId = userId, viewType = viewType });
        }

        [HttpPost]
        public async Task<IActionResult> Unlike(int postId, string viewType)
        {
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            if (await _postService.IsPostLikedByUserAsync(userId, postId))
            {
                await _postService.UnlikePostAsync(userId, postId);
            }

            return RedirectToAction("Profile", new { userId = userId, viewType = viewType });
        }

        [HttpPost]
        [Authorize]  // Ensure only authenticated users can access this
        public async Task<IActionResult> Update(PostViewModel model, string viewType)
        {
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            if (model.UserId != userId)
                return Forbid();

            var post = await _postService.GetByIdAsync(model.Id);
            if (post == null)
                return NotFound();

            post.Content = model.Content;
            post.VideoUrl = model.VideoUrl;
            post.Location = model.Location;

            try
            {
                await _postService.UpdateAsync(post);
                return RedirectToAction("Profile", new { userId = userId, viewType = viewType });
            }
            catch (Exception)
            {
                return View("Index", model); // Handle error feedback as needed
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id, string viewType)
        {
            var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");

            var post = await _postService.GetByIdAsync(id);
            if (post == null || post.UserId != userId)
                return Forbid();

            await _postService.DeleteAsync(id);
            return RedirectToAction("Profile", new { userId = userId, viewType = viewType });
        }
    }
}