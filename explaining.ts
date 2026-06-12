// recommendation-explanation.ts

export interface RecommendationReason {
  code: string;
  message: string;
}

export interface CandidateFeatures {
  isFollowing?: boolean;
  likedAuthor?: boolean;
  ageHours?: number;
  engagement?: number;
  velocity?: number;
}

export function generateRecommendationReasons(
  features: CandidateFeatures
): RecommendationReason[] {
  const reasons: RecommendationReason[] = [];

  if (features.isFollowing) {
    reasons.push({
      code: "FOLLOWING_AUTHOR",
      message: "Bạn đang theo dõi tác giả này",
    });
  }

  if (features.likedAuthor) {
    reasons.push({
      code: "LIKED_AUTHOR_BEFORE",
      message: "Bạn đã từng thích bài viết từ tác giả này",
    });
  }

  if ((features.engagement ?? 0) > 100) {
    reasons.push({
      code: "POPULAR_POST",
      message: "Bài viết đang nhận được nhiều tương tác",
    });
  }

  if ((features.velocity ?? 0) > 20) {
    reasons.push({
      code: "TRENDING_POST",
      message: "Bài viết đang thịnh hành",
    });
  }

  if ((features.ageHours ?? 999) < 6) {
    reasons.push({
      code: "RECENT_POST",
      message: "Bài viết mới được đăng gần đây",
    });
  }

  if (reasons.length === 0) {
    reasons.push({
      code: "GENERAL_RECOMMENDATION",
      message: "Được đề xuất dựa trên hoạt động của bạn",
    });
  }

  return reasons;
}